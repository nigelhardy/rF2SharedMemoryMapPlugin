/*
Implementation of rFactor 2 internal state mapping into shared memory buffers.

Author: The Iron Wolf (vleonavicius@hotmail.com)
Website: thecrewchief.org


Acknowledgements:
  This work is based on:
    - rF2 Internals Plugin sample #7 by ISI/S397 found at: https://www.studio-397.com/modding-resources/
    - rF1 Shared Memory Map Plugin by Dan Allongo found at: https://github.com/dallongo/rFactorSharedMemoryMap


Shared resources:
  This plugin uses double buffering and mutex to allow optional synchronized access to rF2 exposed internal state.
  Shared resources use the following naming convention:
    - $rFactor2SMMP_<BUFFER_TYPE>Buffer1$
    - $rFactor2SMMP_<BUFFER_TYPE>Buffer2$
    - Global\$rFactor2SMMP_<BUFFER_TYPE>Mutex - mutex for optional weak synchronization (see Synchronization below)

  where <BUFFER_TYPE> is one of the following:
    * Telemetry - mapped view of rF2Telemetry structure
    * Scoring - mapped view of rF2Scoring structure
    * Extended - mapped view of rF2Extended structure

  Those types are (with few exceptions) exact mirror of ISI structures, plugin constantly memcpy'es them from game to memory mapped files.


State updates (buffer flips, see Double Buffering):
  Telemetry - updated every 10ms, but in practice only every other update contains updated data, so real update rate is around 50FPS.
  Scoring - every 200ms (5FPS)
  Extended - every 200ms or on tracked function call.

  Plugin does not add artificial delays, except:
    - telemetry updates with same game time are skipped
    - if telemetry mutex is signaled, telemetry buffer update is skipped


Telemetry state:
  rF2 calls UpdateTelemetry for each vehicle.  Plugin tries to guess when all vehicles received an update, and only after that flip is attempted (see Double Buffering).


Extended state:
  Exposed extended state consists of the two parts:

  * Non periodically updated game state:
      Physics settings updates and various callback based properties are tracked.

  * Heuristic data exposed as an attempt to compensate for values not currently available from the game:
      Damage state is tracked, since game provides no accumulated damage data.  Tracking happens on _every_ telemetry/scoring
      update for full precision.
      
  See SharedMemoryPlugin::ExtendedStateTracker struct for details.


Double Buffering:
  Plugin maps each exposed structure into two memory mapped files.  Buffers are written to alternatively.
  rF2MappedBufferHeaders::mCurrentRead indicates last updated buffer.

  Buffers are flipped after each update (see State Updates) except for telemetry state buffers.

  Telemetry buffer flip is designed so that we try to avoid waiting on the mutex if it is signaled.  There are three
  attempts before wait will happen.  Retries only happen on new telemetry frame completion (or skip due to no changes).


Synchronization:
  Important: do not use synchronization if your application:
    - queries for data at high rate (50ms or smaller gaps)
    - does not need consistent view of the whole buffer.  Typically, Dashboards,  varios visualizers do not need such views,
      because partially correct data will be overritten by next frame.  Abusing synchronization might cause game FPS drops.

  A lot of effort was done to ensure minimal impact on the rF2.  Therefore, using mutex does not guarantee that buffer
  won't be overwritten. While mutex is exposed for synchronized access, plugin tries to minimize wait time by retrying
  during telemetry updates (~90FPS) and only waiting for 1ms max during scoring updates (and on fourth telemetry flip retry), 
  before forcefully flipping buffers.  Also, if 1ms elapses on synchronized flip, buffer will be overwritten anyway.


Configuration file:
  Optional configuration file is supported (primarily for debugging purposes).
  See SharedMemoryPlugin::LoadConfig.


Sample consumption:
  For sample C# client, see Monitor\rF2SMMonitor\rF2SMMonitor\MainForm.cs
*/
#include "rFactor2SharedMemoryMap.hpp"          // corresponding header file
#include <stdlib.h>
#include <cstddef>                              // offsetof

double TicksNow();

static double const MILLISECONDS_IN_SECOND = 1000.0;
static double const MICROSECONDS_IN_MILLISECOND = 1000.0;
static double const MICROSECONDS_IN_SECOND = MILLISECONDS_IN_SECOND * MICROSECONDS_IN_MILLISECOND;

DebugLevel SharedMemoryPlugin::msDebugOutputLevel = DebugLevel::Off;
bool SharedMemoryPlugin::msDebugISIInternals = false;
DWORD SharedMemoryPlugin::msMillisMutexWait = 1;

FILE* SharedMemoryPlugin::msDebugFile;
FILE* SharedMemoryPlugin::msIsiTelemetryFile;
FILE* SharedMemoryPlugin::msIsiScoringFile;

// Future/V2:  split into telemetry/scoring/rules etc.
// _Telemetry - possibly no need to interpolate.
// _Scoring
// _Extended
// _Rules
// _Weather
char const* const SharedMemoryPlugin::MM_TELEMETRY_FILE_NAME1 = "$rFactor2SMMP_TelemetryBuffer1$";
char const* const SharedMemoryPlugin::MM_TELEMETRY_FILE_NAME2 = "$rFactor2SMMP_TelemetryBuffer2$";
char const* const SharedMemoryPlugin::MM_TELEMETRY_FILE_ACCESS_MUTEX = R"(Global\$rFactor2SMMP_TelemeteryMutex)";

char const* const SharedMemoryPlugin::MM_SCORING_FILE_NAME1 = "$rFactor2SMMP_ScoringBuffer1$";
char const* const SharedMemoryPlugin::MM_SCORING_FILE_NAME2 = "$rFactor2SMMP_ScoringBuffer2$";
char const* const SharedMemoryPlugin::MM_SCORING_FILE_ACCESS_MUTEX = R"(Global\$rFactor2SMMP_ScoringMutex)";

char const* const SharedMemoryPlugin::MM_EXTENDED_FILE_NAME1 = "$rFactor2SMMP_ExtendedBuffer1$";
char const* const SharedMemoryPlugin::MM_EXTENDED_FILE_NAME2 = "$rFactor2SMMP_ExtendedBuffer2$";
char const* const SharedMemoryPlugin::MM_EXTENDED_FILE_ACCESS_MUTEX = R"(Global\$rFactor2SMMP_ExtendedMutex)";

char const* const SharedMemoryPlugin::CONFIG_FILE_REL_PATH = R"(\UserData\player\rf2smmp.ini)";  // Relative to rF2 root.
char const* const SharedMemoryPlugin::INTERNALS_TELEMETRY_FILENAME = "RF2SMMP_InternalsTelemetryOutput.txt";
char const* const SharedMemoryPlugin::INTERNALS_SCORING_FILENAME = "RF2SMMP_InternalsScoringOutput.txt";
char const* const SharedMemoryPlugin::DEBUG_OUTPUT_FILENAME = "RF2SMMP_DebugOutput.txt";

// plugin information
extern "C" __declspec(dllexport)
const char * __cdecl GetPluginName() { return PLUGIN_NAME_AND_VERSION; }

extern "C" __declspec(dllexport)
PluginObjectType __cdecl GetPluginType() { return(PO_INTERNALS); }

extern "C" __declspec(dllexport)
int __cdecl GetPluginVersion() { return(7); } // InternalsPluginV07 functionality (if you change this return value, you must derive from the appropriate class!)

extern "C" __declspec(dllexport)
PluginObject * __cdecl CreatePluginObject() { return((PluginObject *) new SharedMemoryPlugin); }

extern "C" __declspec(dllexport)
void __cdecl DestroyPluginObject(PluginObject *obj) { delete((SharedMemoryPlugin *)obj); }


//////////////////////////////////////
// SharedMemoryPlugin class
//////////////////////////////////////

SharedMemoryPlugin::SharedMemoryPlugin()
  : mTelemetry(SharedMemoryPlugin::MAX_ASYNC_RETRIES
     , SharedMemoryPlugin::MM_TELEMETRY_FILE_NAME1
     , SharedMemoryPlugin::MM_TELEMETRY_FILE_NAME2
     , SharedMemoryPlugin::MM_TELEMETRY_FILE_ACCESS_MUTEX),
    mScoring(0 /*maxRetries*/
      , SharedMemoryPlugin::MM_SCORING_FILE_NAME1
      , SharedMemoryPlugin::MM_SCORING_FILE_NAME2
      , SharedMemoryPlugin::MM_SCORING_FILE_ACCESS_MUTEX),
    mExtended(0 /*maxRetries*/
      , SharedMemoryPlugin::MM_EXTENDED_FILE_NAME1
      , SharedMemoryPlugin::MM_EXTENDED_FILE_NAME2
      , SharedMemoryPlugin::MM_EXTENDED_FILE_ACCESS_MUTEX)
{}


void SharedMemoryPlugin::Startup(long version)
{
  // Read configuration .ini if there's one.
  LoadConfig();

  char temp[80] = {};
  sprintf(temp, "-STARTUP- (version %.3f)", (float)version / 1000.0f);
  WriteToAllExampleOutputFiles("w", temp);

  if (!mTelemetry.Initialize()) {
    DEBUG_MSG(DebugLevel::Errors, "Failed to initialize telemetry mapping");
    return;
  }

  if (!mScoring.Initialize()) {
    DEBUG_MSG(DebugLevel::Errors, "Failed to initialize scoring mapping");
    return;
  }

  if (!mExtended.Initialize()) {
    DEBUG_MSG(DebugLevel::Errors, "Failed to initialize extended mapping");
    return;
  }

  mIsMapped = true;

  ClearState();

  DEBUG_MSG(DebugLevel::Errors, "Files mapped successfully");
  if (SharedMemoryPlugin::msDebugOutputLevel != DebugLevel::Off) {
    char sizeSz[20] = {};
    auto size = static_cast<int>(sizeof(rF2Telemetry));
    _itoa_s(size, sizeSz, 10);
    DEBUG_MSG3(DebugLevel::Errors, "Size of telemetry buffers:", sizeSz, "bytes each.");

    assert(sizeof(rF2Telemetry) == offsetof(rF2Telemetry, mVehicles[rF2MappedBufferHeader::MAX_MAPPED_VEHICLES]));

    sizeSz[0] = '\0';
    size = static_cast<int>(sizeof(rF2Scoring));
    _itoa_s(size, sizeSz, 10);
    DEBUG_MSG3(DebugLevel::Errors, "Size of scoring buffers:", sizeSz, "bytes each.");

    assert(sizeof(rF2Scoring) == offsetof(rF2Scoring, mVehicles[rF2MappedBufferHeader::MAX_MAPPED_VEHICLES]));

    sizeSz[0] = '\0';
    size = static_cast<int>(sizeof(rF2Extended));
    _itoa_s(size, sizeSz, 10);
    DEBUG_MSG3(DebugLevel::Errors, "Size of extended buffers:", sizeSz, "bytes each.");
  }
}

void SharedMemoryPlugin::Shutdown()
{
  WriteToAllExampleOutputFiles("a", "-SHUTDOWN-");

  DEBUG_MSG(DebugLevel::Errors, "Shutting down");

  if (msDebugFile != nullptr) {
    fclose(msDebugFile);
    msDebugFile = nullptr;
  }

  if (msIsiTelemetryFile != nullptr) {
    fclose(msIsiTelemetryFile);
    msIsiTelemetryFile = nullptr;
  }

  if (msIsiScoringFile != nullptr) {
    fclose(msIsiScoringFile);
    msIsiScoringFile = nullptr;
  }

  mTelemetry.ClearState(nullptr /*pInitialContents*/);
  mTelemetry.ReleaseResources();

  mScoring.ClearState(nullptr /*pInitialContents*/);
  mScoring.ReleaseResources();

  mExtended.ClearState(nullptr /*pInitialContents*/);
  mExtended.ReleaseResources();

  mIsMapped = false;
}

void SharedMemoryPlugin::ClearTimingsAndCounters()
{
  mLastTelemetryUpdateMillis = 0.0;
  mLastScoringUpdateMillis = 0.0;

  mLastTelemetryUpdateET = 0.0;
  mLastScoringUpdateET = 0.0;

  mTelemetryUpdateInProgress = false;
  mCurTelemetryVehicleIndex = 0;

  memset(mParticipantTelemetryUpdated, 0, sizeof(mParticipantTelemetryUpdated));

  mScoringNumVehicles = 0;
}


void SharedMemoryPlugin::ClearState()
{
  if (!mIsMapped)
    return;

  mTelemetry.ClearState(nullptr /*pInitialContents*/);
  mScoring.ClearState(nullptr /*pInitialContents*/);

  // Certain members of extended state persist between restarts/sessions.
  // So, clear the state but pass persisting state as initial state.
  mExtStateTracker.ClearState();
  mExtended.ClearState(&(mExtStateTracker.mExtended));

  ClearTimingsAndCounters();
}

void SharedMemoryPlugin::StartSession()
{
  WriteToAllExampleOutputFiles("a", "--STARTSESSION--");

  ClearState();
}


void SharedMemoryPlugin::EndSession()
{
  WriteToAllExampleOutputFiles("a", "--ENDSESSION--");

  ClearState();
}


void SharedMemoryPlugin::UpdateInRealtimeFC(bool inRealTime)
{
  if (!mIsMapped)
    return;

  DEBUG_MSG(DebugLevel::Synchronization, inRealTime ? "Entering Realtime" : "Exiting Realtime");

  mExtStateTracker.mExtended.mInRealtimeFC = inRealTime;
  memcpy(mExtended.mpCurWriteBuf, &(mExtStateTracker.mExtended), sizeof(rF2Extended));
  mExtended.FlipBuffers();
}


void SharedMemoryPlugin::EnterRealtime()
{
  // start up timer every time we enter realtime
  WriteToAllExampleOutputFiles("a", "---ENTERREALTIME---");

  UpdateInRealtimeFC(true /*inRealtime*/);
}


void SharedMemoryPlugin::ExitRealtime()
{
  WriteToAllExampleOutputFiles("a", "---EXITREALTIME---");

  UpdateInRealtimeFC(false /*inRealtime*/);
}

// Using GTC64 produces 7x larger average interpolation delta (roughly from 5cm to 35cm).
// The max offset stays close, so it might not matter much.
// So, let's keep QPC and see if it causes problems (FPS cost)?
#define USE_QPC
double TicksNow() {
#ifdef USE_QPC
  static double frequencyMicrosecond = 0.0;
  static bool once = false;
  if (!once) {
    LARGE_INTEGER qpcFrequency = {};
    QueryPerformanceFrequency(&qpcFrequency);
    frequencyMicrosecond = static_cast<double>(qpcFrequency.QuadPart) / MICROSECONDS_IN_SECOND;
    once = true;
  }

  LARGE_INTEGER now = {};
  QueryPerformanceCounter(&now);
  return static_cast<double>(now.QuadPart) / frequencyMicrosecond;
#else 
  return GetTickCount64() * MICROSECONDS_IN_MILLISECOND;
#endif
}


void SharedMemoryPlugin::TelemetryTraceSkipUpdate(TelemInfoV01 const& info) const
{
  if (SharedMemoryPlugin::msDebugOutputLevel >= DebugLevel::Timing) {
    char msg[512] = {};
    sprintf(msg, "TELEMETRY - Skipping update due to no changes in the input data.  New ET: %f  Prev ET:%f", info.mElapsedTime, mLastTelemetryUpdateET);
    DEBUG_MSG(DebugLevel::Timing, msg);
  }

  if (SharedMemoryPlugin::msDebugOutputLevel >= DebugLevel::Warnings 
    && !mTelemetry.RetryPending()) { // If we're in retry mode, below check makes no sense as read buffer is behind current state.
    if (info.mPos.x != mTelemetry.mpCurReadBuf->mVehicles->mPos.x
      || info.mPos.y != mTelemetry.mpCurReadBuf->mVehicles->mPos.y
      || info.mPos.z != mTelemetry.mpCurReadBuf->mVehicles->mPos.z)
    {
      char msg[512] = {};
      sprintf(msg, "WARNING - Pos Mismatch on skip update!!!  New ET: %f  Prev ET:%f  Prev Pos: %f %f %f  New Pos %f %f %f", info.mElapsedTime, mLastTelemetryUpdateET,
        info.mPos.x, info.mPos.y, info.mPos.z,
        mTelemetry.mpCurReadBuf->mVehicles->mPos.x,
        mTelemetry.mpCurReadBuf->mVehicles->mPos.y,
        mTelemetry.mpCurReadBuf->mVehicles->mPos.z);
      DEBUG_MSG(DebugLevel::Warnings, msg);
    }
  }
}


void SharedMemoryPlugin::TelemetryTraceBeginUpdate(double telUpdateET)
{
  auto ticksNow = 0.0;
  if (SharedMemoryPlugin::msDebugOutputLevel >= DebugLevel::Timing) {
    ticksNow = TicksNow();
    auto const delta = ticksNow - mLastTelemetryUpdateMillis;

    char msg[512] = {};
    sprintf(msg, "TELEMETRY - Begin Update: Buffer %s.  ET:%f  Delta since last update:%f",
      mTelemetry.mpCurWriteBuf == mTelemetry.mpBuf1 ? "1" : "2", telUpdateET, delta / MICROSECONDS_IN_SECOND);
    
    DEBUG_MSG(DebugLevel::Timing, msg);
  }

  mLastTelemetryUpdateMillis = ticksNow;
}


void SharedMemoryPlugin::TelemetryTraceVehicleAdded(TelemInfoV01 const& info) const
{
  if (SharedMemoryPlugin::msDebugOutputLevel == DebugLevel::Verbose) {
    bool const samePos = info.mPos.x == mTelemetry.mpCurReadBuf->mVehicles[mCurTelemetryVehicleIndex - 1].mPos.x
      && info.mPos.y == mTelemetry.mpCurReadBuf->mVehicles[mCurTelemetryVehicleIndex - 1].mPos.y
      && info.mPos.z == mTelemetry.mpCurReadBuf->mVehicles[mCurTelemetryVehicleIndex - 1].mPos.z;

    char msg[512] = {};
    sprintf(msg, "Telemetry added - mID:%d  ET:%f  Pos Changed:%s", info.mID, info.mElapsedTime, samePos ? "Same" : "Changed");
    DEBUG_MSG(DebugLevel::Verbose, msg);
  }
}


void SharedMemoryPlugin::TelemetryTraceEndUpdate(int numVehiclesInChain) const
{
  if (SharedMemoryPlugin::msDebugOutputLevel >= DebugLevel::Timing) {
    auto const ticksNow = TicksNow();
    auto const deltaSysTimeMicroseconds = ticksNow - mLastTelemetryUpdateMillis;

    char msg[512] = {};
    sprintf(msg, "TELEMETRY - End Update.  Telemetry chain update took %f:  Vehicles in chain: %d", deltaSysTimeMicroseconds / MICROSECONDS_IN_SECOND, numVehiclesInChain);

    DEBUG_MSG(DebugLevel::Timing, msg);
  }
}


void SharedMemoryPlugin::TelemetryFlipBuffers()
{
  if (mLastTelemetryUpdateET <= mLastScoringUpdateET) {
    // If scoring update is ahead of this telemetry update, force flip.
    DEBUG_MSG(DebugLevel::Synchronization, "TELEMETRY - Force flip due to: mLastTelemetryUpdateET <= mLastScoringUpdateET.");
    mTelemetry.FlipBuffers();
  }
  else if (mTelemetry.AsyncRetriesLeft() > 0) {
    auto const retryPending = mTelemetry.RetryPending();
    // Otherwise, try buffer flip.
    mTelemetry.TryFlipBuffers();

    // Print msg about buffer flip failure or success.
    if (mTelemetry.RetryPending())
      DEBUG_INT2(DebugLevel::Synchronization, "TELEMETRY - Buffer flip failed, retries remaining:", mTelemetry.AsyncRetriesLeft());
    else {
      if (retryPending)
        DEBUG_MSG(DebugLevel::Synchronization, "TELEMETRY - Buffer flip succeeded on retry.");
      else
        DEBUG_MSG(DebugLevel::Timing, "TELEMETRY - Buffer flip succeeded.");
    }
  }
  else {
    // Force flip if no more retries are left
    assert(mTelemetry.AsyncRetriesLeft() == 0);
    DEBUG_MSG(DebugLevel::Synchronization, "TELEMETRY - Force flip due to retry limit exceeded.");
    mTelemetry.FlipBuffers();
  }
}


/*
rF2 sends telemetry updates for each vehicle.  The problem is that I do not know when all vehicles received an update.
Below I am trying to flip buffers per-frame, where frame means all vehicles received telemetry update.

I am detecting frame end in two ways:
  * Count vehicles from mID == 0 to mScoringNumVehicles.
  * As a backup for case where mID == 0 drops out of the session, I use mParticipantTelemetryUpdated index to detect the loop.

There's one more check that can be done - 10ms since update chain start will also work, but I am trying to avoid call to QPC.

Note that I am seeing different ET for vehicles in frame (typically no more than 2 values), no idea WTF that is.
*/
void SharedMemoryPlugin::UpdateTelemetry(TelemInfoV01 const& info)
{
  WriteTelemetryInternals(info);

  if (!mIsMapped)
    return;

  auto const partiticpantIndex = min(info.mID, MAX_PARTICIPANT_SLOTS - 1);
  auto const alreadyUpdated = mParticipantTelemetryUpdated[partiticpantIndex];
  if (info.mID == 0 || alreadyUpdated) {
    if (info.mElapsedTime == mLastTelemetryUpdateET) {
      TelemetryTraceSkipUpdate(info);
      assert(!mTelemetryUpdateInProgress);

      // Once per skipped update, retry pending flip, if any.
      if (mTelemetry.RetryPending()) {
        DEBUG_MSG(DebugLevel::Synchronization, "TELEMETRY - Retry pending buffer flip on update skip.");
        TelemetryFlipBuffers();
      }

      // Skip this update, there's no change in data (in most cases).
      return;
    }

    // I saw zis vence and want to understand WTF??
    if (info.mElapsedTime < mLastTelemetryUpdateET)
      DEBUG_MSG(DebugLevel::Warnings, "WARNING: info.mElapsedTime < mLastTelemetryUpdateET");

    TelemetryTraceBeginUpdate(info.mElapsedTime);

    // Ok, this is the new sequence of telemetry updates, and it contains updated data (new ET).

    // First, trace unusual cases as I need to better understand them better.
    // Previous chain did not end.
    if (mCurTelemetryVehicleIndex != 0)
      DEBUG_INT2(DebugLevel::Synchronization, "TELEMETRY - Previous update ended at:", mCurTelemetryVehicleIndex);

    // This is the case where cases where mID == 0 is not in the chain and we hit a loop. 
    if (alreadyUpdated)
      DEBUG_INT2(DebugLevel::Synchronization, "TELEMETRY - Update chain started at:", info.mID);

    // Start new telemetry update chain.
    mLastTelemetryUpdateET = info.mElapsedTime;
    mTelemetryUpdateInProgress = true;
    mCurTelemetryVehicleIndex = 0;
    memset(mParticipantTelemetryUpdated, 0, sizeof(mParticipantTelemetryUpdated));
    mTelemetry.mpCurWriteBuf->mNumVehicles = mScoringNumVehicles;
  }

  if (mTelemetryUpdateInProgress) {
    // Update extended state for this vehicle.
    // Since I do not want to miss impact data, and it is not accumulated in any way
    // I am aware of in rF2 internals, process on every telemetr update.
    mExtStateTracker.ProcessTelemetryUpdate(info);

    auto const partiticpantIndex = min(info.mID, MAX_PARTICIPANT_SLOTS - 1);
    assert(mParticipantTelemetryUpdated[partiticpantIndex] == false);
    mParticipantTelemetryUpdated[partiticpantIndex] = true;

    memcpy(&(mTelemetry.mpCurWriteBuf->mVehicles[mCurTelemetryVehicleIndex]), &info, sizeof(rF2VehicleTelemetry));
    ++mCurTelemetryVehicleIndex;

    TelemetryTraceVehicleAdded(info);

    // See if this is the last vehicle to update.
    if (mCurTelemetryVehicleIndex >= mTelemetry.mpCurWriteBuf->mNumVehicles
      || mCurTelemetryVehicleIndex >= rF2Telemetry::MAX_MAPPED_VEHICLES) {
      auto const numVehiclesInChain = mCurTelemetryVehicleIndex;

      mTelemetry.mpCurWriteBuf->mBytesUpdatedHint = offsetof(rF2Telemetry, mVehicles[mTelemetry.mpCurWriteBuf->mNumVehicles]);

      mTelemetryUpdateInProgress = false;
      mCurTelemetryVehicleIndex = 0;
      memset(mParticipantTelemetryUpdated, 0, sizeof(mParticipantTelemetryUpdated));

      TelemetryFlipBuffers();
      TelemetryTraceEndUpdate(numVehiclesInChain);
    }

    return;
  }
}


void SharedMemoryPlugin::ScoringTraceBeginUpdate()
{
  auto ticksNow = 0.0;
  if (SharedMemoryPlugin::msDebugOutputLevel >= DebugLevel::Timing) {
    ticksNow = TicksNow();
    auto const delta = ticksNow - mLastScoringUpdateMillis;

    if (mScoring.mpCurWriteBuf == mScoring.mpBuf1)
      DEBUG_FLOAT2(DebugLevel::Timing, "SCORING - Begin Update: Buffer 1.  Delta since last update:", delta / MICROSECONDS_IN_SECOND);
    else
      DEBUG_FLOAT2(DebugLevel::Timing, "SCORING - Begin Update: Buffer 2.  Delta since last update:", delta / MICROSECONDS_IN_SECOND);

    char msg[512] = {};
    sprintf(msg, "SCORING - Scoring ET:%f  Telemetry ET:%f", mLastScoringUpdateET, mLastTelemetryUpdateET);
    DEBUG_MSG(DebugLevel::Timing, msg);
  }

  mLastScoringUpdateMillis = ticksNow;
}


void SharedMemoryPlugin::UpdateScoring(ScoringInfoV01 const& info)
{
  WriteScoringInternals(info);

  if (!mIsMapped)
    return;

  mScoringNumVehicles = info.mNumVehicles;
  mLastScoringUpdateET = info.mCurrentET;

  ScoringTraceBeginUpdate();

  if (mTelemetry.RetryPending()) {
    DEBUG_MSG(DebugLevel::Synchronization, "SCORING - Force telemetry flip due to retry pending.");
    mTelemetry.FlipBuffers();
  }

  // Below apparently never happens, but let's keep it in case there's a regression in the game.
  // So far, this appears to only happen on session end, when telemetry is already zeroed out.
  if (mLastScoringUpdateET > mLastTelemetryUpdateET)
    DEBUG_MSG(DebugLevel::Warnings, "WARNING: Scoring update is ahead of telemetry.");

  memcpy(&(mScoring.mpCurWriteBuf->mScoringInfo), &info, sizeof(rF2ScoringInfo));

  for (int i = 0; i < info.mNumVehicles; ++i)
    memcpy(&(mScoring.mpCurWriteBuf->mVehicles[i]), &(info.mVehicle[i]), sizeof(rF2VehicleScoring));

  mScoring.mpCurWriteBuf->mBytesUpdatedHint = offsetof(rF2Scoring, mVehicles[info.mNumVehicles]);

  mScoring.FlipBuffers();

  // Update extended state.
  mExtStateTracker.ProcessScoringUpdate(info);
  memcpy(mExtended.mpCurWriteBuf, &(mExtStateTracker.mExtended), sizeof(rF2Extended));
  mExtended.FlipBuffers();
}


// Invoked periodically.
bool SharedMemoryPlugin::WantsToDisplayMessage(MessageInfoV01& /*msgInfo*/)
{
  // Looks like this is write only API, can't read current text in MC
  return false;
}


void SharedMemoryPlugin::UpdateThreadState(long type, bool starting)
{
  (type == 0 ? mExtStateTracker.mExtended.mMultimediaThreadStarted : mExtStateTracker.mExtended.mSimulationThreadStarted)
    = starting;

  if (!mIsMapped)
    return;

  memcpy(mExtended.mpCurWriteBuf, &(mExtStateTracker.mExtended), sizeof(rF2Extended));
  mExtended.FlipBuffers();
}


void SharedMemoryPlugin::ThreadStarted(long type)
{
  DEBUG_MSG(DebugLevel::Synchronization, type == 0 ? "Multimedia thread started" : "Simulation thread started");
  UpdateThreadState(type, true /*starting*/);
}

void SharedMemoryPlugin::ThreadStopping(long type)
{
  DEBUG_MSG(DebugLevel::Synchronization, type == 0 ? "Multimedia thread stopped" : "Simulation thread stopped");
  UpdateThreadState(type, false /*starting*/);
}


// Called roughly every 300ms.
bool SharedMemoryPlugin::AccessTrackRules(TrackRulesV01& /*info*/)
{
  return false;
}

// Invoked periodically.
bool SharedMemoryPlugin::AccessPitMenu(PitMenuV01& /*info*/)
{
  return false;
}

void SharedMemoryPlugin::SetPhysicsOptions(PhysicsOptionsV01& options)
{
  DEBUG_MSG(DebugLevel::Timing, "PHYSICS - Updated.");
  memcpy(&(mExtStateTracker.mExtended.mPhysics), &options, sizeof(rF2PhysicsOptions));
  memcpy(mExtended.mpCurWriteBuf, &(mExtStateTracker.mExtended), sizeof(rF2Extended));
  mExtended.FlipBuffers();
}

////////////////////////////////////////////
// Config, files and debugging output helpers.
////////////////////////////////////////////
void SharedMemoryPlugin::LoadConfig()
{
  char wd[MAX_PATH] = {};
  GetCurrentDirectory(MAX_PATH, wd);

  auto iniPath = lstrcatA(wd, SharedMemoryPlugin::CONFIG_FILE_REL_PATH);

  auto outputLvl = GetPrivateProfileInt("config", "debugOutputLevel", 0, iniPath);
  if (outputLvl > DebugLevel::Verbose)
    outputLvl = 0;

  msDebugOutputLevel = static_cast<DebugLevel>(outputLvl);
  if (msDebugOutputLevel > 0)
    remove(SharedMemoryPlugin::DEBUG_OUTPUT_FILENAME);  // Remove previous output.

  msDebugISIInternals = GetPrivateProfileInt("config", "debugISIInternals", 0, iniPath) != 0;

  DEBUG_MSG2(DebugLevel::Verbose, "Loaded config from:", iniPath);
}

void SharedMemoryPlugin::WriteToAllExampleOutputFiles(const char * const openStr, const char * const msg)
{
  if (!SharedMemoryPlugin::msDebugISIInternals)
    return;

  auto fo = fopen(SharedMemoryPlugin::INTERNALS_TELEMETRY_FILENAME, openStr);
  if (fo != nullptr) {
    fprintf(fo, "%s\n", msg);
    fclose(fo);
  }

  fo = fopen(SharedMemoryPlugin::INTERNALS_SCORING_FILENAME, openStr);
  if (fo != nullptr) {
    fprintf(fo, "%s\n", msg);
    fclose(fo);
  }
}

void SharedMemoryPlugin::WriteDebugMsg(DebugLevel lvl, const char* const format, ...)
{
  if (lvl > SharedMemoryPlugin::msDebugOutputLevel)
    return;

  va_list argList;
  if (SharedMemoryPlugin::msDebugFile == nullptr) {
    SharedMemoryPlugin::msDebugFile = _fsopen(SharedMemoryPlugin::DEBUG_OUTPUT_FILENAME, "a", _SH_DENYNO);
    setvbuf(SharedMemoryPlugin::msDebugFile, nullptr, _IOFBF, SharedMemoryPlugin::BUFFER_IO_BYTES);
  }

  fprintf(SharedMemoryPlugin::msDebugFile, "TID:0x%04x  ", GetCurrentThreadId());
  if (SharedMemoryPlugin::msDebugFile != nullptr) {
    va_start(argList, format);
    vfprintf(SharedMemoryPlugin::msDebugFile, format, argList);
    va_end(argList);
  }

  // Flush periodically for low volume messages.
  static ULONGLONG lastFlushTicks = 0uLL;
  auto const ticksNow = GetTickCount64();
  if ((ticksNow - lastFlushTicks) / MILLISECONDS_IN_SECOND > DEBUG_IO_FLUSH_PERIOD_SECS) {
    fflush(SharedMemoryPlugin::msDebugFile);
    lastFlushTicks = ticksNow;
  }
}