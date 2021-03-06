﻿/*
TransitionTracker class various state transitions in rF2 state and optionally logs transitions to files.

Author: The Iron Wolf (vleonavicius@hotmail.com)
Website: thecrewchief.org
*/
using rF2SMMonitor.rFactor2Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static rF2SMMonitor.rFactor2Constants;

namespace rF2SMMonitor
{
  internal class TransitionTracker
  {
    private static readonly string fileTimesTampString = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    private static readonly string basePath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\logs";
    private static readonly string phaseAndStateTrackingFilePath = $"{basePath}\\{fileTimesTampString}___PhaseAndStateTracking.log";
    private static readonly string damageTrackingFilePath = $"{basePath}\\{fileTimesTampString}___DamageTracking.log";
    private static readonly string phaseAndStateDeltaTrackingFilePath = $"{basePath}\\{fileTimesTampString}___PhaseAndStateTrackingDelta.log";
    private static readonly string damageTrackingDeltaFilePath = $"{basePath}\\{fileTimesTampString}___DamageTrackingDelta.log";
    private static readonly string timingTrackingFilePath = $"{basePath}\\{fileTimesTampString}___TimingTracking.log";

    internal TransitionTracker()
    {
      if (!Directory.Exists(basePath))
        Directory.CreateDirectory(basePath);
    }

    private string GetEnumString<T>(sbyte value)
    {
      var enumType = typeof(T);

      var enumValue = (T)Enum.ToObject(enumType, value);
      return Enum.IsDefined(enumType, enumValue) ? $"{enumValue.ToString()}({value})" : string.Format("Unknown({0})", value);
    }

    private string GetEnumString<T>(byte value)
    {
      var enumType = typeof(T);

      var enumValue = (T)Enum.ToObject(enumType, value);
      return Enum.IsDefined(enumType, enumValue) ? $"{enumValue.ToString()}({value})" : string.Format("Unknown({0})", value);
    }

    private string GetSessionString(int session)
    {
      // current session (0=testday 1-4=practice 5-8=qual 9=warmup 10-13=race)
      if (session == 0)
        return $"TestDay({session})";
      else if (session >= 1 && session <= 4)
        return $"Practice({session})";
      else if (session >= 5 && session <= 8)
        return $"Qualification({session})";
      else if (session == 9)
        return $"WarmUp({session})";
      else if (session >= 10 && session <= 13)
        return $"Race({session})";

      return $"Unknown({session})";
    }


    // TODO: Telemetry section
    // Telemetry values (separate section)

    internal class PhaseAndState
    {
      internal rF2GamePhase mGamePhase = (rF2GamePhase)Enum.ToObject(typeof(rF2GamePhase), -255);
      internal int mSession = -255;
      internal rF2YellowFlagState mYellowFlagState = (rF2YellowFlagState)Enum.ToObject(typeof(rF2YellowFlagState), -255);
      internal int mSector = -255;
      internal int mCurrentSector = -255;
      internal byte mInRealTimeFC = 255;
      internal byte mInRealTime = 255;
      internal rF2YellowFlagState mSector1Flag = (rF2YellowFlagState)Enum.ToObject(typeof(rF2YellowFlagState), -255);
      internal rF2YellowFlagState mSector2Flag = (rF2YellowFlagState)Enum.ToObject(typeof(rF2YellowFlagState), -255);
      internal rF2YellowFlagState mSector3Flag = (rF2YellowFlagState)Enum.ToObject(typeof(rF2YellowFlagState), -255);
      internal rF2Control mControl;
      internal byte mInPits = 255;
      internal byte mIsPlayer = 255;
      internal int mPlace = -255;
      internal rF2PitState mPitState = (rF2PitState)Enum.ToObject(typeof(rF2PitState), -255);
      internal rF2GamePhase mIndividualPhase = (rF2GamePhase)Enum.ToObject(typeof(rF2GamePhase), -255);
      internal rF2PrimaryFlag mFlag = (rF2PrimaryFlag)Enum.ToObject(typeof(rF2PrimaryFlag), -255);
      internal byte mUnderYellow = 255;
      internal rF2CountLapFlag mCountLapFlag = (rF2CountLapFlag)Enum.ToObject(typeof(rF2CountLapFlag), -255);
      internal byte mInGarageStall = 255;
      internal rF2FinishStatus mFinishStatus = (rF2FinishStatus)Enum.ToObject(typeof(rF2FinishStatus), -255);
      internal int mLapNumber = -255;
      internal short mTotalLaps = -255;
      internal int mMaxLaps = -1;
      internal int mNumVehicles = -1;
      internal byte mScheduledStops = 255;
      internal byte mHeadlights = 255;
      internal byte mSpeedLimiter = 255;
      internal byte mFrontTireCompoundIndex = 255;
      internal byte mRearTireCompoundIndex = 255;
      internal string mFrontTireCompoundName = "Unknown";
      internal string mRearTireCompoundName = "Unknown";
      internal byte mFrontFlapActivated = 255;
      internal byte mRearFlapActivated = 255;
      internal rF2RearFlapLegalStatus mRearFlapLegalStatus = (rF2RearFlapLegalStatus)Enum.ToObject(typeof(rF2RearFlapLegalStatus), -255);
      internal rF2IgnitionStarterStatus mIgnitionStarter = (rF2IgnitionStarterStatus)Enum.ToObject(typeof(rF2IgnitionStarterStatus), -255);
      internal byte mSpeedLimiterAvailable = 255;
      internal byte mAntiStallActivated = 255;
      internal byte mStartLight = 255;
      internal byte mNumRedLights = 255;
      internal short mNumPitstops = -255;
      internal short mNumPenalties = -255;
      internal int mLapsBehindNext = -1;
      internal int mLapsBehindLeader = -1;
      internal byte mPlayerHeadlights = 255;
      internal byte mServerScored = 255;
      internal int mQualification = -1;
    }

    internal PhaseAndState prevPhaseAndSate = new PhaseAndState();
    internal StringBuilder sbPhaseChanged = new StringBuilder();
    internal StringBuilder sbPhaseLabel = new StringBuilder();
    internal StringBuilder sbPhaseValues = new StringBuilder();
    internal StringBuilder sbPhaseChangedCol2 = new StringBuilder();
    internal StringBuilder sbPhaseLabelCol2 = new StringBuilder();
    internal StringBuilder sbPhaseValuesCol2 = new StringBuilder();

    rF2GamePhase lastDamageTrackingGamePhase = (rF2GamePhase)Enum.ToObject(typeof(rF2GamePhase), -255);
    rF2GamePhase lastPhaseTrackingGamePhase = (rF2GamePhase)Enum.ToObject(typeof(rF2GamePhase), -255);
    rF2GamePhase lastTimingTrackingGamePhase = (rF2GamePhase)Enum.ToObject(typeof(rF2GamePhase), -255);

    private float screenYStart = 170.0f;

    internal void TrackPhase(ref rF2Scoring scoring, ref rF2Telemetry telemetry, ref rF2Extended extended, Graphics g, bool logToFile)
    {
      if (logToFile)
      {
        if ((this.lastPhaseTrackingGamePhase == rF2GamePhase.Garage
              || this.lastPhaseTrackingGamePhase == rF2GamePhase.SessionOver
              || this.lastPhaseTrackingGamePhase == rF2GamePhase.SessionStopped
              || (int)this.lastPhaseTrackingGamePhase == 9)  // What is 9? 
            && ((rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Countdown
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Formation
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GridWalk
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GreenFlag))
        {
          var lines = new List<string>();
          lines.Add("\n");
          lines.Add("************************************************************************************");
          lines.Add("* NEW SESSION **********************************************************************");
          lines.Add("************************************************************************************");
          File.AppendAllLines(phaseAndStateTrackingFilePath, lines);
          File.AppendAllLines(phaseAndStateDeltaTrackingFilePath, lines);
        }
      }

      this.lastPhaseTrackingGamePhase = (rF2GamePhase)scoring.mScoringInfo.mGamePhase;

      if (scoring.mScoringInfo.mNumVehicles == 0)
        return;

      // Build map of mID -> telemetry.mVehicles[i]. 
      // They are typically matching values, however, we need to handle online cases and dropped vehicles (mID can be reused).
      var idsToTelIndices = new Dictionary<long, int>();
      for (int i = 0; i < telemetry.mNumVehicles; ++i)
      {
        if (!idsToTelIndices.ContainsKey(telemetry.mVehicles[i].mID))
          idsToTelIndices.Add(telemetry.mVehicles[i].mID, i);
      }

      var scoringPlrId = scoring.mVehicles[0].mID;
      if (!idsToTelIndices.ContainsKey(scoringPlrId))
        return;

      var resolvedIdx = idsToTelIndices[scoringPlrId];
      var playerVeh = scoring.mVehicles[0];
      var playerVehTelemetry = telemetry.mVehicles[resolvedIdx];

      var ps = new PhaseAndState();

      ps.mGamePhase = (rF2GamePhase)scoring.mScoringInfo.mGamePhase;
      ps.mSession = scoring.mScoringInfo.mSession;
      ps.mYellowFlagState = (rF2YellowFlagState)scoring.mScoringInfo.mYellowFlagState;
      ps.mSector = playerVeh.mSector == 0 ? 3 : playerVeh.mSector;
      ps.mCurrentSector = playerVehTelemetry.mCurrentSector;
      ps.mInRealTime = scoring.mScoringInfo.mInRealtime;
      ps.mInRealTimeFC = extended.mInRealtimeFC;
      ps.mSector1Flag = (rF2YellowFlagState)scoring.mScoringInfo.mSectorFlag[0];
      ps.mSector2Flag = (rF2YellowFlagState)scoring.mScoringInfo.mSectorFlag[1];
      ps.mSector3Flag = (rF2YellowFlagState)scoring.mScoringInfo.mSectorFlag[2];
      ps.mControl = (rF2Control)playerVeh.mControl;
      ps.mInPits = playerVeh.mInPits;
      ps.mIsPlayer = playerVeh.mIsPlayer;
      ps.mPlace = playerVeh.mPlace;
      ps.mPitState = (rF2PitState)playerVeh.mPitState;
      ps.mIndividualPhase = (rF2GamePhase)playerVeh.mIndividualPhase;
      ps.mFlag = (rF2PrimaryFlag)playerVeh.mFlag;
      ps.mUnderYellow = playerVeh.mUnderYellow;
      ps.mCountLapFlag = (rF2CountLapFlag)playerVeh.mCountLapFlag;
      ps.mInGarageStall = playerVeh.mInGarageStall;
      ps.mFinishStatus = (rF2FinishStatus)playerVeh.mFinishStatus;
      ps.mLapNumber = playerVehTelemetry.mLapNumber;
      ps.mTotalLaps = playerVeh.mTotalLaps;
      ps.mMaxLaps = scoring.mScoringInfo.mMaxLaps;
      ps.mNumVehicles = scoring.mScoringInfo.mNumVehicles;
      ps.mScheduledStops = playerVehTelemetry.mScheduledStops;
      ps.mHeadlights = playerVeh.mHeadlights;
      ps.mSpeedLimiter = playerVehTelemetry.mSpeedLimiter;
      ps.mFrontTireCompoundIndex = playerVehTelemetry.mFrontTireCompoundIndex;
      ps.mRearTireCompoundIndex = playerVehTelemetry.mRearTireCompoundIndex;
      ps.mFrontTireCompoundName = TransitionTracker.getStringFromBytes(playerVehTelemetry.mFrontTireCompoundName);
      ps.mRearTireCompoundName = TransitionTracker.getStringFromBytes(playerVehTelemetry.mRearTireCompoundName);
      ps.mFrontFlapActivated = playerVehTelemetry.mFrontFlapActivated;
      ps.mRearFlapActivated = playerVehTelemetry.mRearFlapActivated;
      ps.mRearFlapLegalStatus = (rF2RearFlapLegalStatus)playerVehTelemetry.mRearFlapLegalStatus;
      ps.mIgnitionStarter = (rF2IgnitionStarterStatus)playerVehTelemetry.mIgnitionStarter;
      ps.mSpeedLimiterAvailable = playerVehTelemetry.mSpeedLimiterAvailable;
      ps.mAntiStallActivated = playerVehTelemetry.mAntiStallActivated;
      ps.mStartLight = scoring.mScoringInfo.mStartLight;
      ps.mNumRedLights = scoring.mScoringInfo.mNumRedLights;
      ps.mNumPitstops = playerVeh.mNumPitstops;
      ps.mNumPenalties = playerVeh.mNumPenalties;
      ps.mLapsBehindNext = playerVeh.mLapsBehindNext;
      ps.mLapsBehindLeader = playerVeh.mLapsBehindLeader;
      ps.mPlayerHeadlights = playerVeh.mHeadlights;
      ps.mServerScored = playerVeh.mServerScored;
      ps.mQualification = playerVeh.mQualification;

      // Only refresh UI if there's change.
      if (this.prevPhaseAndSate.mGamePhase != ps.mGamePhase
        || this.prevPhaseAndSate.mSession != ps.mSession
        || this.prevPhaseAndSate.mYellowFlagState != ps.mYellowFlagState
        || this.prevPhaseAndSate.mSector != ps.mSector
        || this.prevPhaseAndSate.mCurrentSector != ps.mCurrentSector
        || this.prevPhaseAndSate.mInRealTimeFC != ps.mInRealTimeFC
        || this.prevPhaseAndSate.mInRealTime != ps.mInRealTime
        || this.prevPhaseAndSate.mSector1Flag != ps.mSector1Flag
        || this.prevPhaseAndSate.mSector2Flag != ps.mSector2Flag
        || this.prevPhaseAndSate.mSector3Flag != ps.mSector3Flag
        || this.prevPhaseAndSate.mControl != ps.mControl
        || this.prevPhaseAndSate.mInPits != ps.mInPits
        || this.prevPhaseAndSate.mIsPlayer != ps.mIsPlayer
        || this.prevPhaseAndSate.mPlace != ps.mPlace
        || this.prevPhaseAndSate.mPitState != ps.mPitState
        || this.prevPhaseAndSate.mIndividualPhase != ps.mIndividualPhase
        || this.prevPhaseAndSate.mFlag != ps.mFlag
        || this.prevPhaseAndSate.mUnderYellow != ps.mUnderYellow
        || this.prevPhaseAndSate.mCountLapFlag != ps.mCountLapFlag
        || this.prevPhaseAndSate.mInGarageStall != ps.mInGarageStall
        || this.prevPhaseAndSate.mFinishStatus != ps.mFinishStatus
        || this.prevPhaseAndSate.mLapNumber != ps.mLapNumber
        || this.prevPhaseAndSate.mTotalLaps != playerVeh.mTotalLaps
        || this.prevPhaseAndSate.mMaxLaps != ps.mMaxLaps
        || this.prevPhaseAndSate.mNumVehicles != ps.mNumVehicles
        || this.prevPhaseAndSate.mScheduledStops != ps.mScheduledStops
        || this.prevPhaseAndSate.mHeadlights != ps.mHeadlights
        || this.prevPhaseAndSate.mSpeedLimiter != ps.mSpeedLimiter
        || this.prevPhaseAndSate.mFrontTireCompoundIndex != ps.mFrontTireCompoundIndex
        || this.prevPhaseAndSate.mRearTireCompoundIndex != ps.mRearTireCompoundIndex
        || this.prevPhaseAndSate.mFrontTireCompoundName != ps.mFrontTireCompoundName
        || this.prevPhaseAndSate.mRearTireCompoundName != ps.mRearTireCompoundName
        || this.prevPhaseAndSate.mFrontFlapActivated != ps.mFrontFlapActivated
        || this.prevPhaseAndSate.mRearFlapActivated != ps.mRearFlapActivated
        || this.prevPhaseAndSate.mRearFlapLegalStatus != ps.mRearFlapLegalStatus
        || this.prevPhaseAndSate.mIgnitionStarter != ps.mIgnitionStarter
        || this.prevPhaseAndSate.mSpeedLimiterAvailable != ps.mSpeedLimiterAvailable
        || this.prevPhaseAndSate.mAntiStallActivated != ps.mAntiStallActivated
        || this.prevPhaseAndSate.mStartLight != ps.mStartLight
        || this.prevPhaseAndSate.mNumRedLights != ps.mNumRedLights
        || this.prevPhaseAndSate.mNumPitstops != ps.mNumPitstops
        || this.prevPhaseAndSate.mNumPenalties != ps.mNumPenalties
        || this.prevPhaseAndSate.mLapsBehindNext != ps.mLapsBehindNext
        || this.prevPhaseAndSate.mLapsBehindLeader != ps.mLapsBehindLeader
        || this.prevPhaseAndSate.mPlayerHeadlights != ps.mHeadlights
        || this.prevPhaseAndSate.mServerScored != ps.mServerScored
        || this.prevPhaseAndSate.mQualification != ps.mQualification)
      {
        this.sbPhaseChanged = new StringBuilder();
        sbPhaseChanged.Append((this.prevPhaseAndSate.mGamePhase != ps.mGamePhase ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSession != ps.mSession ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mYellowFlagState != ps.mYellowFlagState ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSector != ps.mSector ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mCurrentSector != ps.mCurrentSector ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mInRealTimeFC != ps.mInRealTimeFC ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mInRealTime != ps.mInRealTime ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSector1Flag != ps.mSector1Flag ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSector2Flag != ps.mSector2Flag ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSector3Flag != ps.mSector3Flag ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mControl != ps.mControl ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mInPits != ps.mInPits ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mIsPlayer != ps.mIsPlayer ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mPlace != ps.mPlace ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mPitState != ps.mPitState ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mIndividualPhase != ps.mIndividualPhase ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mFlag != ps.mFlag ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mUnderYellow != ps.mUnderYellow ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mCountLapFlag != ps.mCountLapFlag ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mInGarageStall != ps.mInGarageStall ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mFinishStatus != ps.mFinishStatus ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mLapNumber != ps.mLapNumber ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mTotalLaps != ps.mTotalLaps ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mMaxLaps != ps.mMaxLaps ? "***\n" : "\n"));

        this.sbPhaseChangedCol2 = new StringBuilder();
        sbPhaseChangedCol2.Append((this.prevPhaseAndSate.mNumVehicles != ps.mNumVehicles ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mScheduledStops != ps.mScheduledStops ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mHeadlights != ps.mHeadlights ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSpeedLimiter != ps.mSpeedLimiter ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mFrontTireCompoundIndex != ps.mFrontTireCompoundIndex ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mRearTireCompoundIndex != ps.mRearTireCompoundIndex ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mFrontTireCompoundName != ps.mFrontTireCompoundName ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mRearTireCompoundName != ps.mRearTireCompoundName ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mFrontFlapActivated != ps.mFrontFlapActivated ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mRearFlapActivated != ps.mRearFlapActivated ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mRearFlapLegalStatus != ps.mRearFlapLegalStatus ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mIgnitionStarter != ps.mIgnitionStarter ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mSpeedLimiterAvailable != ps.mSpeedLimiter ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mAntiStallActivated != ps.mAntiStallActivated ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mStartLight != ps.mStartLight ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mNumRedLights != ps.mNumRedLights ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mNumPitstops != ps.mNumPitstops ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mNumPenalties != ps.mNumPenalties ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mLapsBehindNext != ps.mLapsBehindNext ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mLapsBehindLeader != ps.mLapsBehindLeader ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mPlayerHeadlights != ps.mPlayerHeadlights ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mServerScored != ps.mServerScored ? "***\n" : "\n")
          + (this.prevPhaseAndSate.mQualification != ps.mQualification ? "***\n" : "\n"));

        // Save current phase and state.
        this.prevPhaseAndSate = ps;

        this.sbPhaseLabel = new StringBuilder();
        sbPhaseLabel.Append("mGamePhase:\n"
          + "mSession:\n"
          + "mYellowFlagState:\n"
          + "mSector:\n"
          + "mCurrentSector:\n"
          + "mInRealtimeFC:\n"
          + "mInRealtimeSU:\n"
          + "mSectorFlag[0]:\n"
          + "mSectorFlag[1]:\n"
          + "mSectorFlag[2]:\n"
          + "mControl:\n"
          + "mInPits:\n"
          + "mIsPlayer:\n"
          + "mPlace:\n"
          + "mPitState:\n"
          + "mIndividualPhase:\n"
          + "mFlag:\n"
          + "mUnderYellow:\n"
          + "mCountLapFlag:\n"
          + "mInGarageStall:\n"
          + "mFinishStatus:\n"
          + "mLapNumber:\n"
          + "mTotalLaps:\n"
          + "mMaxLaps:\n");

        this.sbPhaseLabelCol2 = new StringBuilder();
        sbPhaseLabelCol2.Append("mNumVehicles:\n"
          + "mScheduledStops:\n"
          + "mHeadlights:\n"
          + "mSpeedLimiter:\n"
          + "mFrontTireCompoundIndex:\n"
          + "mRearTireCompoundIndex:\n"
          + "mFrontTireCompoundName:\n"
          + "mRearTireCompoundName:\n"
          + "mFrontFlapActivated:\n"
          + "mRearFlapActivated:\n"
          + "mRearFlapLegalStatus:\n"
          + "mIgnitionStarter:\n"
          + "mSpeedLimiterAvailable:\n"
          + "mAntiStallActivated:\n"
          + "mStartLight:\n"
          + "mNumRedLights:\n"
          + "mNumPitstops:\n"
          + "mNumPenalties:\n"
          + "mLapsBehindNext:\n"
          + "mLapsBehindLeader:\n"
          + "mPlayerHeadlights:\n"
          + "mServerScored:\n"
          + "mQualification:\n"
        );

        this.sbPhaseValues = new StringBuilder();
        sbPhaseValues.Append(
          $"{GetEnumString<rF2GamePhase>(scoring.mScoringInfo.mGamePhase)}\n"
          + $"{GetSessionString(scoring.mScoringInfo.mSession)}\n"
          + $"{GetEnumString<rF2YellowFlagState>(scoring.mScoringInfo.mYellowFlagState)}\n"
          + $"{ps.mSector}\n"
          + $"0x{ps.mCurrentSector,4:X8}\n" // {4:X} hexadecimal to see values
          + (ps.mInRealTimeFC == 0 ? $"false({ps.mInRealTimeFC})" : $"true({ps.mInRealTimeFC})") + "\n"
          + (ps.mInRealTime == 0 ? $"false({ps.mInRealTime})" : $"true({ps.mInRealTime})") + "\n"
          + $"{GetEnumString<rF2YellowFlagState>(scoring.mScoringInfo.mSectorFlag[0])}\n"
          + $"{GetEnumString<rF2YellowFlagState>(scoring.mScoringInfo.mSectorFlag[1])}\n"
          + $"{GetEnumString<rF2YellowFlagState>(scoring.mScoringInfo.mSectorFlag[2])}\n"
          + $"{GetEnumString<rF2Control>(playerVeh.mControl)}\n"
          + (ps.mInPits == 0 ? $"false({ps.mInPits})" : $"true({ps.mInPits})") + "\n"
          + (ps.mIsPlayer == 0 ? $"false({ps.mIsPlayer})" : $"true({ps.mIsPlayer})") + "\n"
          + $"{ps.mPlace}\n"
          + $"{GetEnumString<rF2PitState>(playerVeh.mPitState)}\n"
          + $"{GetEnumString<rF2GamePhase>(playerVeh.mIndividualPhase)}\n"
          + $"{GetEnumString<rF2PrimaryFlag>(playerVeh.mFlag)}\n"
          + $"{ps.mUnderYellow}\n"
          + $"{GetEnumString<rF2CountLapFlag>(playerVeh.mCountLapFlag)}\n"
          + (ps.mInGarageStall == 0 ? $"false({ps.mInGarageStall})" : $"true({ps.mInGarageStall})") + "\n"
          + $"{GetEnumString<rF2FinishStatus>(playerVeh.mFinishStatus)}\n"
          + $"{ps.mLapNumber}\n"
          + $"{ps.mTotalLaps}\n"
          + $"{ps.mMaxLaps}\n");

        this.sbPhaseValuesCol2 = new StringBuilder();
        sbPhaseValuesCol2.Append($"{ps.mNumVehicles}\n"
          + (ps.mScheduledStops == 0 ? $"false({ps.mScheduledStops})" : $"true({ps.mScheduledStops})") + "\n"
          + (ps.mHeadlights == 0 ? $"false({ps.mHeadlights})" : $"true({ps.mHeadlights})") + "\n"
          + (ps.mSpeedLimiter == 0 ? $"false({ps.mSpeedLimiter})" : $"true({ps.mSpeedLimiter})") + "\n"
          + (ps.mFrontTireCompoundIndex == 0 ? $"false({ps.mFrontTireCompoundIndex})" : $"true({ps.mFrontTireCompoundIndex})") + "\n"
          + (ps.mRearTireCompoundIndex == 0 ? $"false({ps.mRearTireCompoundIndex})" : $"true({ps.mRearTireCompoundIndex})") + "\n"
          + $"{ps.mFrontTireCompoundName}\n"
          + $"{ps.mRearTireCompoundName}\n"
          + (ps.mFrontFlapActivated == 0 ? $"false({ps.mFrontFlapActivated})" : $"true({ps.mFrontFlapActivated})") + "\n"
          + (ps.mRearFlapActivated == 0 ? $"false({ps.mRearFlapActivated})" : $"true({ps.mRearFlapActivated})") + "\n"
          + $"{GetEnumString<rF2RearFlapLegalStatus>(playerVehTelemetry.mRearFlapLegalStatus)}\n"
          + $"{GetEnumString<rF2IgnitionStarterStatus>(playerVehTelemetry.mIgnitionStarter)}\n"
          + (ps.mSpeedLimiterAvailable == 0 ? $"false({ps.mSpeedLimiterAvailable})" : $"true({ps.mSpeedLimiterAvailable})") + "\n"
          + (ps.mAntiStallActivated == 0 ? $"false({ps.mAntiStallActivated})" : $"true({ps.mAntiStallActivated})") + "\n"
          + $"{ps.mStartLight}\n"
          + $"{ps.mNumRedLights}\n"
          + $"{ps.mNumPitstops}\n"
          + $"{ps.mNumPenalties}\n"
          + $"{ps.mLapsBehindNext}\n"
          + $"{ps.mLapsBehindLeader}\n"
          + (ps.mPlayerHeadlights == 0 ? $"false({ps.mPlayerHeadlights})" : $"true({ps.mPlayerHeadlights})") + "\n"
          + (ps.mServerScored == 0 ? $"false({ps.mServerScored})" : $"true({ps.mServerScored})") + "\n"
          + $"{ps.mQualification}\n");

        if (logToFile)
        {
          var changed = this.sbPhaseChanged.ToString().Split('\n');
          var labels = this.sbPhaseLabel.ToString().Split('\n');
          var values = this.sbPhaseValues.ToString().Split('\n');

          var changedCol2 = this.sbPhaseChangedCol2.ToString().Split('\n');
          var labelsCol2 = this.sbPhaseLabelCol2.ToString().Split('\n');
          var valuesCol2 = this.sbPhaseValuesCol2.ToString().Split('\n');

          var list = new List<string>(changed);
          list.AddRange(changedCol2);
          changed = list.ToArray();

          list = new List<string>(labels);
          list.AddRange(labelsCol2);
          labels = list.ToArray();

          list = new List<string>(values);
          list.AddRange(valuesCol2);
          values = list.ToArray();

          Debug.Assert(changed.Length == labels.Length && values.Length == labels.Length);

          var lines = new List<string>();
          var updateTime = DateTime.Now.ToString();

          lines.Add($"\n{updateTime}");
          for (int i = 0; i < changed.Length; ++i)
            lines.Add($"{changed[i]}{labels[i]}{values[i]}");

          File.AppendAllLines(phaseAndStateTrackingFilePath, lines);

          lines.Clear();

          lines.Add($"\n{updateTime}");
          for (int i = 0; i < changed.Length; ++i)
          {
            if (changed[i].StartsWith("***"))
              lines.Add($"{changed[i]}{labels[i]}{values[i]}");
          }

          File.AppendAllLines(phaseAndStateDeltaTrackingFilePath, lines);
        }
      }

      if (g != null)
      {
        g.DrawString(this.sbPhaseChanged.ToString(), SystemFonts.DefaultFont, Brushes.Orange, 3.0f, this.screenYStart + 3.0f);
        g.DrawString(this.sbPhaseLabel.ToString(), SystemFonts.DefaultFont, Brushes.Green, 30.0f, this.screenYStart);
        g.DrawString(this.sbPhaseValues.ToString(), SystemFonts.DefaultFont, Brushes.Purple, 130.0f, this.screenYStart);

        g.DrawString(this.sbPhaseChangedCol2.ToString(), SystemFonts.DefaultFont, Brushes.Orange, 253.0f, this.screenYStart + 3.0f);
        g.DrawString(this.sbPhaseLabelCol2.ToString(), SystemFonts.DefaultFont, Brushes.Green, 280.0f, this.screenYStart);
        g.DrawString(this.sbPhaseValuesCol2.ToString(), SystemFonts.DefaultFont, Brushes.Purple, 430.0f, this.screenYStart);
      }
    }

    private static string getStringFromBytes(byte[] bytes)
    {
      if (bytes == null)
        return "";

      var nullIdx = Array.IndexOf(bytes, (byte)0);

      return nullIdx >= 0
        ? Encoding.Default.GetString(bytes, 0, nullIdx)
        : Encoding.Default.GetString(bytes);
    }

    internal class DamageInfo
    {
      internal byte[] mDentSeverity = new byte[8];         // dent severity at 8 locations around the car (0=none, 1=some, 2=more)
      internal double mLastImpactMagnitude = -1.0;   // magnitude of last impact
      internal double mAccumulatedImpactMagnitude = -1.0;   // magnitude of last impact
      internal double mMaxImpactMagnitude = -1.0;   // magnitude of last impact
      internal rF2Vec3 mLastImpactPos;        // location of last impact
      internal double mLastImpactET = -1.0;          // time of last impact
      internal byte mOverheating = 255;            // whether overheating icon is shown
      internal byte mDetached = 255;               // whether any parts (besides wheels) have been detached
      //internal byte mHeadlights = 255;             // whether headlights are on

      internal byte mFrontLeftFlat = 255;                    // whether tire is flat
      internal byte mFrontLeftDetached = 255;                // whether wheel is detached
      internal byte mFrontRightFlat = 255;                    // whether tire is flat
      internal byte mFrontRightDetached = 255;                // whether wheel is detached

      internal byte mRearLeftFlat = 255;                    // whether tire is flat
      internal byte mRearLeftDetached = 255;                // whether wheel is detached
      internal byte mRearRightFlat = 255;                    // whether tire is flat
      internal byte mRearRightDetached = 255;                // whether wheel is detached
    }

    internal DamageInfo prevDamageInfo = new DamageInfo();
    internal StringBuilder sbDamageChanged = new StringBuilder();
    internal StringBuilder sbDamageLabel = new StringBuilder();
    internal StringBuilder sbDamageValues = new StringBuilder();

    internal void TrackDamage(ref rF2Scoring scoring, ref rF2Telemetry telemetry, ref rF2Extended extended, Graphics g, bool logToFile)
    {
      if (logToFile)
      {
        if ((this.lastDamageTrackingGamePhase == rF2GamePhase.Garage
              || this.lastDamageTrackingGamePhase == rF2GamePhase.SessionOver
              || this.lastDamageTrackingGamePhase == rF2GamePhase.SessionStopped
              || (int)this.lastDamageTrackingGamePhase == 9)  // What is 9? 
            && ((rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Countdown
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Formation
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GridWalk
              || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GreenFlag))
        {
          var lines = new List<string>();
          lines.Add("\n");
          lines.Add("************************************************************************************");
          lines.Add("* NEW SESSION **********************************************************************");
          lines.Add("************************************************************************************");
          File.AppendAllLines(damageTrackingFilePath, lines);
          File.AppendAllLines(damageTrackingDeltaFilePath, lines);
        }
      }

      this.lastDamageTrackingGamePhase = (rF2GamePhase)scoring.mScoringInfo.mGamePhase;

      if (scoring.mScoringInfo.mNumVehicles == 0)
        return;

      // Build map of mID -> telemetry.mVehicles[i]. 
      // They are typically matching values, however, we need to handle online cases and dropped vehicles (mID can be reused).
      var idsToTelIndices = new Dictionary<long, int>();
      for (int i = 0; i < telemetry.mNumVehicles; ++i)
      {
        if (!idsToTelIndices.ContainsKey(telemetry.mVehicles[i].mID))
          idsToTelIndices.Add(telemetry.mVehicles[i].mID, i);
      }

      var scoringPlrId = scoring.mVehicles[0].mID;
      if (!idsToTelIndices.ContainsKey(scoringPlrId))
        return;

      var resolvedIdx = idsToTelIndices[scoringPlrId];
      var playerVeh = scoring.mVehicles[0];
      var playerVehTelemetry = telemetry.mVehicles[resolvedIdx];

      var di = new DamageInfo();
      di.mDentSeverity = playerVehTelemetry.mDentSeverity;
      di.mLastImpactMagnitude = playerVehTelemetry.mLastImpactMagnitude;
      di.mAccumulatedImpactMagnitude = extended.mTrackedDamages[playerVehTelemetry.mID].mAccumulatedImpactMagnitude;
      di.mMaxImpactMagnitude = extended.mTrackedDamages[playerVehTelemetry.mID].mMaxImpactMagnitude;
      di.mLastImpactPos = playerVehTelemetry.mLastImpactPos;
      di.mLastImpactET = playerVehTelemetry.mLastImpactET;
      di.mOverheating = playerVehTelemetry.mOverheating;
      di.mDetached = playerVehTelemetry.mDetached;
      di.mFrontLeftFlat = playerVehTelemetry.mWheels[(int)rF2WheelIndex.FrontLeft].mFlat;
      di.mFrontLeftDetached = playerVehTelemetry.mWheels[(int)rF2WheelIndex.FrontLeft].mDetached;
      di.mFrontRightFlat = playerVehTelemetry.mWheels[(int)rF2WheelIndex.FrontRight].mFlat;
      di.mFrontRightDetached = playerVehTelemetry.mWheels[(int)rF2WheelIndex.FrontRight].mDetached;
      di.mRearLeftFlat = playerVehTelemetry.mWheels[(int)rF2WheelIndex.RearLeft].mFlat;
      di.mRearLeftDetached = playerVehTelemetry.mWheels[(int)rF2WheelIndex.RearLeft].mDetached;
      di.mRearRightFlat = playerVehTelemetry.mWheels[(int)rF2WheelIndex.RearRight].mFlat;
      di.mRearRightDetached = playerVehTelemetry.mWheels[(int)rF2WheelIndex.RearRight].mDetached;

      bool dentSevChanged = false;
      for (int i = 0; i < 8; ++i) {
        if (this.prevDamageInfo.mDentSeverity[i] != di.mDentSeverity[i]) {
          dentSevChanged = true;
          break;
        }
      }

      bool lastImpactPosChanged = di.mLastImpactPos.x != this.prevDamageInfo.mLastImpactPos.x
        || di.mLastImpactPos.y != this.prevDamageInfo.mLastImpactPos.y
        || di.mLastImpactPos.z != this.prevDamageInfo.mLastImpactPos.z;

      // Only refresh UI if there's change.
      if (dentSevChanged
        || di.mLastImpactMagnitude != this.prevDamageInfo.mLastImpactMagnitude
        || di.mAccumulatedImpactMagnitude != this.prevDamageInfo.mAccumulatedImpactMagnitude
        || di.mMaxImpactMagnitude != this.prevDamageInfo.mMaxImpactMagnitude
        || lastImpactPosChanged
        || di.mLastImpactET != this.prevDamageInfo.mLastImpactET
        || di.mOverheating != this.prevDamageInfo.mOverheating
        || di.mDetached != this.prevDamageInfo.mDetached
        || di.mFrontLeftFlat != this.prevDamageInfo.mFrontLeftFlat
        || di.mFrontRightFlat != this.prevDamageInfo.mFrontRightFlat
        || di.mRearLeftFlat != this.prevDamageInfo.mRearLeftFlat
        || di.mRearRightFlat != this.prevDamageInfo.mRearRightFlat
        || di.mFrontLeftDetached != this.prevDamageInfo.mFrontLeftDetached
        || di.mFrontRightDetached != this.prevDamageInfo.mFrontRightDetached
        || di.mRearLeftDetached != this.prevDamageInfo.mRearLeftDetached
        || di.mRearRightDetached != this.prevDamageInfo.mRearRightDetached)
      {
        this.sbDamageChanged = new StringBuilder();
        sbDamageChanged.Append((dentSevChanged ? "***\n" : "\n")
          + (di.mLastImpactMagnitude != this.prevDamageInfo.mLastImpactMagnitude ? "***\n" : "\n")
          + (di.mAccumulatedImpactMagnitude != this.prevDamageInfo.mAccumulatedImpactMagnitude ? "***\n" : "\n")
          + (di.mMaxImpactMagnitude != this.prevDamageInfo.mMaxImpactMagnitude ? "***\n" : "\n")
          + (lastImpactPosChanged ? "***\n" : "\n")
          + (di.mLastImpactET != this.prevDamageInfo.mLastImpactET ? "***\n" : "\n")
          + (di.mOverheating != this.prevDamageInfo.mOverheating ? "***\n" : "\n")
          + (di.mDetached != this.prevDamageInfo.mDetached ? "***\n" : "\n")
          + ((di.mFrontLeftFlat != this.prevDamageInfo.mFrontLeftFlat
              || di.mFrontRightFlat != this.prevDamageInfo.mFrontRightFlat
              || di.mFrontLeftDetached != this.prevDamageInfo.mFrontLeftDetached
              || di.mFrontRightDetached != this.prevDamageInfo.mFrontRightDetached) ? "***\n" : "\n")
          + ((di.mRearLeftFlat != this.prevDamageInfo.mRearLeftFlat
              || di.mRearRightFlat != this.prevDamageInfo.mRearRightFlat
              || di.mRearLeftDetached != this.prevDamageInfo.mRearLeftDetached
              || di.mRearRightDetached != this.prevDamageInfo.mRearRightDetached) ? "***\n" : "\n"));

        // Save current damage info.
        this.prevDamageInfo = di;

        this.sbDamageLabel = new StringBuilder();
        sbDamageLabel.Append(
          "mDentSeverity:\n"
          + "mLastImpactMagnitude:\n"
          + "mAccumulatedImpactMagnitude:\n"
          + "mMaxImpactMagnitude:\n"
          + "mLastImpactPos:\n"
          + "mLastImpactET:\n"
          + "mOverheating:\n"
          + "mDetached:\n"
          + "Front Wheels:\n"
          + "Rear Wheels:\n");

        this.sbDamageValues = new StringBuilder();
        sbDamageValues.Append(
          $"{di.mDentSeverity[0]},{di.mDentSeverity[1]},{di.mDentSeverity[2]},{di.mDentSeverity[3]},{di.mDentSeverity[4]},{di.mDentSeverity[5]},{di.mDentSeverity[6]},{di.mDentSeverity[7]}\n"
          + $"{di.mLastImpactMagnitude:N1}\n"
          + $"{di.mAccumulatedImpactMagnitude:N1}\n"
          + $"{di.mMaxImpactMagnitude:N1}\n"
          + $"x={di.mLastImpactPos.x:N4} y={di.mLastImpactPos.y:N4} z={di.mLastImpactPos.z:N4}\n"
          + $"{di.mLastImpactET}\n"
          + $"{di.mOverheating}\n"
          + $"{di.mDetached}\n"
          + $"Left Flat:{di.mFrontLeftFlat}    Left Detached:{di.mFrontLeftDetached}        Right Flat:{di.mFrontRightFlat}    Right Detached:{di.mFrontRightDetached}\n"
          + $"Left Flat:{di.mRearLeftFlat}    Left Detached:{di.mRearLeftDetached}        Right Flat:{di.mRearRightFlat}    Right Detached:{di.mRearRightDetached}\n"
          );

        if (logToFile)
        {
          var changed = this.sbDamageChanged.ToString().Split('\n');
          var labels = this.sbDamageLabel.ToString().Split('\n');
          var values = this.sbDamageValues.ToString().Split('\n');
          Debug.Assert(changed.Length == labels.Length && values.Length == labels.Length);

          var lines = new List<string>();

          var updateTime = DateTime.Now.ToString();
          lines.Add($"\n{updateTime}");
          for (int i = 0; i < changed.Length; ++i)
            lines.Add($"{changed[i]}{labels[i]}{values[i]}");

          File.AppendAllLines(damageTrackingFilePath, lines);

          lines.Clear();
          lines.Add($"\n{updateTime}");
          for (int i = 0; i < changed.Length; ++i)
          {
            if (changed[i].StartsWith("***"))
              lines.Add($"{changed[i]}{labels[i]}{values[i]}");
          }

          File.AppendAllLines(damageTrackingDeltaFilePath, lines);
        }
      }

      if (g != null)
      {
        var dmgYStart = this.screenYStart + 310.0f;
        g.DrawString(this.sbDamageChanged.ToString(), SystemFonts.DefaultFont, Brushes.Orange, 3.0f, dmgYStart + 3.0f);
        g.DrawString(this.sbDamageLabel.ToString(), SystemFonts.DefaultFont, Brushes.Green, 30.0f, dmgYStart);
        g.DrawString(this.sbDamageValues.ToString(), SystemFonts.DefaultFont, Brushes.Purple, 200.0f, dmgYStart);
      }
    }

    internal class PlayerTimingInfo
    {
      internal string name = null;
      internal double lastS1Time = -1.0;
      internal double lastS2Time = -1.0;
      internal double lastS3Time = -1.0;

      internal double currS1Time = -1.0;
      internal double currS2Time = -1.0;
      internal double currS3Time = -1.0;

      internal double bestS1Time = -1.0;
      internal double bestS2Time = -1.0;
      internal double bestS3Time = -1.0;

      internal double currLapET = -1.0;
      internal double lastLapTime = -1.0;
      internal double currLapTime = -1.0;
      internal double bestLapTime = -1.0;

      internal int currLap = -1;
    }

    internal class OpponentTimingInfo
    {
      internal string name = null;
      internal int position = -1;
      internal double lastS1Time = -1.0;
      internal double lastS2Time = -1.0;
      internal double lastS3Time = -1.0;

      internal double currS1Time = -1.0;
      internal double currS2Time = -1.0;
      internal double currS3Time = -1.0;

      internal double bestS1Time = -1.0;
      internal double bestS2Time = -1.0;
      internal double bestS3Time = -1.0;

      internal double currLapET = -1.0;
      internal double lastLapTime = -1.0;
      internal double currLapTime = -1.0;
      internal double bestLapTime = -1.0;

      internal int currLap = -1;

      internal string vehicleName = null;
      internal string vehicleClass = null;
    }

    // string -> lap data

    internal class LapData
    {
      internal class LapStats
      {
        internal int lapNumber = -1;
        internal double lapTime = -1.0;
        internal double S1Time = -1.0;
        internal double S2Time = -1.0;
        internal double S3Time = -1.0;
      }

      internal int lastLapCompleted = -1;
      internal List<LapStats> lapStats = new List<LapStats>();
    }

    internal Dictionary<string, LapData> lapDataMap = null;

    int lastTimingSector = -1;
    string bestSplitString = "";

    private int getSector(int rf2Sector) { return rf2Sector == 0 ? 3 : rf2Sector; }
    private string lapTimeStr(double time)
    {
      return time > 0.0 ? TimeSpan.FromSeconds(time).ToString(@"mm\:ss\:fff") : time.ToString();
    }

    internal void TrackTimings(ref rF2Scoring scoring, ref rF2Telemetry telemetry, ref rF2Extended extended, Graphics g, bool logToFile)
    {
      if ((this.lastTimingTrackingGamePhase == rF2GamePhase.Garage
            || this.lastTimingTrackingGamePhase == rF2GamePhase.SessionOver
            || this.lastTimingTrackingGamePhase == rF2GamePhase.SessionStopped
            || (int)this.lastTimingTrackingGamePhase == 9)  // What is 9? 
          && ((rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Countdown
            || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.Formation
            || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GridWalk
            || (rF2GamePhase)scoring.mScoringInfo.mGamePhase == rF2GamePhase.GreenFlag))
      {
        this.lapDataMap = null;
        this.bestSplitString = "";
        if (logToFile)
        {
          var lines = new List<string>();
          lines.Add("\n");
          lines.Add("************************************************************************************");
          lines.Add("* NEW SESSION **********************************************************************");
          lines.Add("************************************************************************************");
          File.AppendAllLines(timingTrackingFilePath, lines);
        }
      }

      this.lastTimingTrackingGamePhase = (rF2GamePhase)scoring.mScoringInfo.mGamePhase;

      if (scoring.mScoringInfo.mNumVehicles == 0)
      {
        this.lastTimingSector = -1;
        this.lapDataMap = null;
        this.bestSplitString = "";

        return;
      }

      if (this.lapDataMap == null)
        this.lapDataMap = new Dictionary<string, LapData>();

      // Build map of mID -> telemetry.mVehicles[i]. 
      // They are typically matching values, however, we need to handle online cases and dropped vehicles (mID can be reused).
      var idsToTelIndices = new Dictionary<long, int>();
      for (int i = 0; i < telemetry.mNumVehicles; ++i)
      {
        if (!idsToTelIndices.ContainsKey(telemetry.mVehicles[i].mID))
          idsToTelIndices.Add(telemetry.mVehicles[i].mID, i);
      }

      var scoringPlrId = scoring.mVehicles[0].mID;
      if (!idsToTelIndices.ContainsKey(scoringPlrId))
        return;

      var resolvedIdx = idsToTelIndices[scoringPlrId];
      var playerVeh = scoring.mVehicles[0];
      var playerVehTelemetry = telemetry.mVehicles[resolvedIdx];

      bool sectorChanged = this.lastTimingSector != this.getSector(playerVeh.mSector);
      bool newLap = this.lastTimingSector == 3 && this.getSector(playerVeh.mSector) == 1;

      this.lastTimingSector = this.getSector(playerVeh.mSector);

      StringBuilder sbPlayer = null;
      PlayerTimingInfo ptiPlayer = null;
      var bls = this.getBestLapStats(TransitionTracker.getStringFromBytes(playerVeh.mDriverName), newLap /*skipLastLap*/);
      this.getDetailedVehTiming("Player:", ref playerVeh, bls, ref scoring, out sbPlayer, out ptiPlayer);

      var opponentInfos = new List<OpponentTimingInfo>();
      for (int i = 0; i < scoring.mScoringInfo.mNumVehicles; ++i)
      {
        var veh = scoring.mVehicles[i];
        var o = new OpponentTimingInfo();
        o.name = TransitionTracker.getStringFromBytes(veh.mDriverName);
        o.position = veh.mPlace;

        o.lastS1Time = veh.mLastSector1 > 0.0 ? veh.mLastSector1 : -1.0;
        o.lastS2Time = veh.mLastSector1 > 0.0 && veh.mLastSector2 > 0.0
          ? veh.mLastSector2 - veh.mLastSector1 : -1.0;
        o.lastS3Time = veh.mLastSector2 > 0.0 && veh.mLastLapTime > 0.0
          ? veh.mLastLapTime - veh.mLastSector2 : -1.0;

        o.currS1Time = o.lastS1Time;
        o.currS2Time = o.lastS2Time;
        o.currS3Time = o.lastS3Time;

        // Check if we have more current values for S1 and S2.
        // S3 always equals to lastS3Time.
        if (veh.mCurSector1 > 0.0)
          o.currS1Time = veh.mCurSector1;

        if (veh.mCurSector1 > 0.0 && veh.mCurSector2 > 0.0)
          o.currS2Time = veh.mCurSector2 - veh.mCurSector1;

        o.bestS1Time = veh.mBestSector1 > 0.0 ? veh.mBestSector1 : -1.0;
        o.bestS2Time = veh.mBestSector1 > 0.0 && veh.mBestSector2 > 0.0 ? veh.mBestSector2 - veh.mBestSector1 : -1.0;

        // Wrong:
        o.bestS3Time = veh.mBestSector2 > 0.0 && veh.mBestLapTime > 0.0 ? veh.mBestLapTime - veh.mBestSector2 : -1.0;

        o.currLapET = veh.mLapStartET;
        o.lastLapTime = veh.mLastLapTime;
        o.currLapTime = scoring.mScoringInfo.mCurrentET - veh.mLapStartET;
        o.bestLapTime = veh.mBestLapTime;
        o.currLap = veh.mTotalLaps;
        o.vehicleName = TransitionTracker.getStringFromBytes(veh.mVehicleName);
        o.vehicleClass = TransitionTracker.getStringFromBytes(veh.mVehicleClass);

        opponentInfos.Add(o);
      }

      // Order by pos, ascending.
      opponentInfos.Sort((o1, o2) => o1.position.CompareTo(o2.position));
      var sbOpponentNames = new StringBuilder();
      sbOpponentNames.Append("Name | Class | Vehicle:\n");
      foreach (var o in opponentInfos)
        sbOpponentNames.Append($"{o.name} | {o.vehicleClass} | {o.vehicleName}\n");

      // Save lap times history.
      for (int i = 0; i < scoring.mScoringInfo.mNumVehicles; ++i)
      {
        var veh = scoring.mVehicles[i];
        var driverName = TransitionTracker.getStringFromBytes(veh.mDriverName);

        // If we don't have this vehicle in a map, add it. (And initialize laps completed).
        if (!this.lapDataMap.ContainsKey(driverName))
        {
          var ldNew = new LapData();
          ldNew.lastLapCompleted = veh.mTotalLaps;
          this.lapDataMap.Add(driverName, ldNew);
        }

        // If this is the new lap for this vehicle, update the lastLapNumber, and save last lap stats.
        var ld = this.lapDataMap[driverName];
        if (ld.lastLapCompleted != veh.mTotalLaps)
        {
          ld.lastLapCompleted = veh.mTotalLaps;

          // Only record valid laps.
          if (veh.mLastLapTime > 0.0)
          {
            var lsNew = new LapData.LapStats
            {
              lapNumber = veh.mTotalLaps,
              lapTime = veh.mLastLapTime,
              S1Time = veh.mLastSector1,
              S2Time = veh.mLastSector2 - veh.mLastSector1,
              S3Time = veh.mLastLapTime - veh.mLastSector2
            };

            ld.lapStats.Add(lsNew);
          }
        }
      }

      // TODO: Remove best Ever values, they're not needed.
      var sbOpponentStats = new StringBuilder();
      sbOpponentStats.Append("Pos:  Lap:      Best Tracked:      Best S1:      Best S2:      Best S3:\n");
      foreach (var o in opponentInfos)
      {
        var skipLastLap = o.name == TransitionTracker.getStringFromBytes(playerVeh.mDriverName) && newLap;
        var bestLapStats = this.getBestLapStats(o.name, skipLastLap);

        var bestLapS1 = bestLapStats.S1Time;
        var bestLapS2 = bestLapStats.S2Time;
        var bestLapS3 = bestLapStats.S3Time;
        var bestLapTimeTracked = bestLapStats.lapTime;

        sbOpponentStats.Append($"{o.position,5}{o.currLap,8}{this.lapTimeStr(bestLapTimeTracked),22:N3}{this.lapTimeStr(bestLapS1),13:N3}{this.lapTimeStr(bestLapS2),13:N3}{this.lapTimeStr(bestLapS3),13:N3}\n");
      }

      // Find fastest vehicle.
      var blsFastest = new LapData.LapStats();
      var fastestName = "";
      foreach (var lapData in this.lapDataMap)
      {
        // If this is the new lap, ignore just completed lap for the player vehicle, and use time of one lap before.
        bool skipLastLap = newLap && lapData.Key == TransitionTracker.getStringFromBytes(playerVeh.mDriverName);

        var blsCandidate = this.getBestLapStats(lapData.Key, skipLastLap);
        if (blsCandidate.lapTime < 0.0)
          continue;

        if (blsFastest.lapTime < 0.0 
          || blsCandidate.lapTime < blsFastest.lapTime)
        {
          fastestName = lapData.Key;
          blsFastest = blsCandidate;
        }
      }

      int fastestIndex = -1;
      for (int i = 0; i < scoring.mScoringInfo.mNumVehicles; ++i)
      {
        if (fastestName == TransitionTracker.getStringFromBytes(scoring.mVehicles[i].mDriverName))
        {
          fastestIndex = i;
          break;
        }
      }

      PlayerTimingInfo ptiFastest = null;
      var sbFastest = new StringBuilder("");
      if (fastestIndex != -1)
      {
        var fastestVeh = scoring.mVehicles[fastestIndex];
        if (blsFastest.lapTime > 0.0)
        {
        //'  var blsFastest = this.getBestLapStats(this.getStringFromBytes(fastestVeh.mDriverName));
          this.getDetailedVehTiming("Fastest:", ref fastestVeh, blsFastest, ref scoring, out sbFastest, out ptiFastest);
        }
      }

      var sbPlayerDeltas = new StringBuilder("");
      if (ptiFastest != null)
      {
        var deltaLapTime = ptiPlayer.bestLapTime - ptiFastest.bestLapTime;
        var deltaS1Time = ptiPlayer.bestS1Time - ptiFastest.bestS1Time;
        var deltaS2Time = ptiPlayer.bestS2Time - ptiFastest.bestS2Time;
        var deltaS3Time = ptiPlayer.bestS3Time - ptiFastest.bestS3Time;

        var deltaSelfLapTime = ptiPlayer.lastLapTime - ptiPlayer.bestLapTime;
        var deltaSelfS1Time = ptiPlayer.currS1Time - ptiPlayer.bestS1Time;
        var deltaSelfS2Time = ptiPlayer.currS2Time - ptiPlayer.bestS2Time;
        var deltaSelfS3Time = ptiPlayer.currS3Time - ptiPlayer.bestS3Time;

        var deltaCurrSelfLapTime = ptiPlayer.lastLapTime - ptiFastest.bestLapTime;
        var deltaCurrSelfS1Time = ptiPlayer.currS1Time - ptiFastest.bestS1Time;
        var deltaCurrSelfS2Time = ptiPlayer.currS2Time - ptiFastest.bestS2Time;
        var deltaCurrSelfS3Time = ptiPlayer.currS3Time - ptiFastest.bestS3Time;

        var deltaCurrSelfLapStr = deltaCurrSelfLapTime > 0.0 ? "+" : "";
        deltaCurrSelfLapStr = deltaCurrSelfLapStr + $"{deltaCurrSelfLapTime:N3}";

        var deltaCurrSelfS1Str = deltaCurrSelfS1Time > 0.0 ? "+" : "";
        deltaCurrSelfS1Str = deltaCurrSelfS1Str + $"{deltaCurrSelfS1Time:N3}";

        var deltaCurrSelfS2Str = deltaCurrSelfS2Time > 0.0 ? "+" : "";
        deltaCurrSelfS2Str = deltaCurrSelfS2Str + $"{deltaCurrSelfS2Time:N3}";

        var deltaCurrSelfS3Str = deltaCurrSelfS3Time > 0.0 ? "+" : "";
        deltaCurrSelfS3Str = deltaCurrSelfS3Str + $"{deltaCurrSelfS3Time:N3}";

        sbPlayerDeltas.Append($"Player delta current vs session best:    deltaCurrSelfBestLapTime: {deltaCurrSelfLapStr}\ndeltaCurrSelfBestS1: {deltaCurrSelfS1Str}    deltaCurrSelfBestS2: {deltaCurrSelfS2Str}    deltaCurrSelfBestS3: {deltaCurrSelfS3Str}\n\n");

        // Once per sector change.
        if (sectorChanged)
        {
          // Calculate "Best Split" to match rFactor 2 HUDs
          var currSector = this.getSector(playerVeh.mSector);
          double bestSplit = 0.0;
          if (currSector == 1)
            bestSplit = ptiPlayer.lastLapTime - ptiFastest.bestLapTime;
          else if (currSector == 2)
            bestSplit = ptiPlayer.currS1Time - ptiFastest.bestS1Time;
          else
            bestSplit = (ptiPlayer.currS1Time + ptiPlayer.currS2Time) - (ptiFastest.bestS1Time + ptiFastest.bestS2Time);

          var bestSplitStr = bestSplit > 0.0 ? "+" : "";
          bestSplitStr += $"{bestSplit:N3}";

          this.bestSplitString = $"Best Split: {bestSplitStr}\n\n";
        }

        sbPlayerDeltas.Append(this.bestSplitString);

        var deltaSelfLapStr = deltaSelfLapTime > 0.0 ? "+" : "";
        deltaSelfLapStr = deltaSelfLapStr + $"{deltaSelfLapTime:N3}";

        var deltaSelfS1Str = deltaSelfS1Time > 0.0 ? "+" : "";
        deltaSelfS1Str = deltaSelfS1Str + $"{deltaSelfS1Time:N3}";

        var deltaSelfS2Str = deltaSelfS2Time > 0.0 ? "+" : "";
        deltaSelfS2Str = deltaSelfS2Str + $"{deltaSelfS2Time:N3}";

        var deltaSelfS3Str = deltaSelfS3Time > 0.0 ? "+" : "";
        deltaSelfS3Str = deltaSelfS3Str + $"{deltaSelfS3Time:N3}";

        sbPlayerDeltas.Append($"Player delta current vs self best:    deltaSelfBestLapTime: {deltaSelfLapStr}\ndeltaSelfBestS1: {deltaSelfS1Str}    deltaSelfBestS2: {deltaSelfS2Str}    deltaBestS3: {deltaSelfS3Str}\n\n");

        var deltaLapStr = deltaLapTime > 0.0 ? "+" : "";
        deltaLapStr = deltaLapStr + $"{deltaLapTime:N3}";

        var deltaS1Str = deltaS1Time > 0.0 ? "+" : "";
        deltaS1Str = deltaS1Str + $"{deltaS1Time:N3}";

        var deltaS2Str = deltaS2Time > 0.0 ? "+" : "";
        deltaS2Str = deltaS2Str + $"{deltaS2Time:N3}";

        var deltaS3Str = deltaS3Time > 0.0 ? "+" : "";
        deltaS3Str = deltaS3Str + $"{deltaS3Time:N3}";

        sbPlayerDeltas.Append($"Player delta best vs session best:    deltaBestLapTime: {deltaLapStr}\ndeltaBestS1: {deltaS1Str}    deltaBestS2: {deltaS2Str}    deltaBestS3: {deltaS3Str}\n\n");
      }

      if (logToFile && sectorChanged)
      {
        var updateTime = DateTime.Now.ToString();
        File.AppendAllText(timingTrackingFilePath, $"\n\n{updateTime}    Sector: {this.lastTimingSector}  ***************************************************** \n\n");

        File.AppendAllText(timingTrackingFilePath, sbPlayer.ToString() + "\n");
        File.AppendAllText(timingTrackingFilePath, sbPlayerDeltas.ToString() + "\n");
        File.AppendAllText(timingTrackingFilePath, sbFastest.ToString() + "\n");

        var names = sbOpponentNames.ToString().Split('\n');
        var stats = sbOpponentStats.ToString().Split('\n');
        var lines = new List<string>();
        Debug.Assert(names.Count() == stats.Count());

        for (int i = 0; i < names.Count(); ++i)
          lines.Add($"{stats[i]}    {names[i]}");

        File.AppendAllLines(timingTrackingFilePath, lines);
      }

      if (g != null)
      {
        var timingsYStart = this.screenYStart + 440.0f;
        g.DrawString(sbPlayer.ToString(), SystemFonts.DefaultFont, Brushes.Magenta, 3.0f, timingsYStart);
        g.DrawString(sbPlayerDeltas.ToString(), SystemFonts.DefaultFont, Brushes.Black, 3.0f, timingsYStart + 90.0f);
        g.DrawString(sbFastest.ToString(), SystemFonts.DefaultFont, Brushes.OrangeRed, 3.0f, timingsYStart + 240.0f);
        g.DrawString(sbOpponentNames.ToString(), SystemFonts.DefaultFont, Brushes.Green, 560.0f, 50.0f);
        g.DrawString(sbOpponentStats.ToString(), SystemFonts.DefaultFont, Brushes.Purple, 850.0f, 50.0f);
      }
    }

    private LapData.LapStats getBestLapStats(string opponentName, bool skipLastLap)
    {
      LapData.LapStats bestLapStats = new LapData.LapStats();
      if (this.lapDataMap.ContainsKey(opponentName))
      {
        var opLd = this.lapDataMap[opponentName];

        double bestLapTimeTracked = -1.0;
        var lapsToCheck = opLd.lapStats.Count;
        if (skipLastLap)
          --lapsToCheck;

        for (int i = 0; i < lapsToCheck; ++i)
        {
          var ls = opLd.lapStats[i];
          if (bestLapStats.lapTime < 0.0
            || ls.lapTime < bestLapTimeTracked)
          {
            bestLapTimeTracked = ls.lapTime;
            bestLapStats = ls;
          }
        }
      }

      return bestLapStats;
    }

    private void getDetailedVehTiming(string name, ref rF2VehicleScoring vehicle, LapData.LapStats bestLapStats, ref rF2Scoring scoring, out StringBuilder sbDetails, out PlayerTimingInfo pti)
    {
      pti = new PlayerTimingInfo();
      pti.name = TransitionTracker.getStringFromBytes(vehicle.mDriverName);
      pti.lastS1Time = vehicle.mLastSector1 > 0.0 ? vehicle.mLastSector1 : -1.0;
      pti.lastS2Time = vehicle.mLastSector1 > 0.0 && vehicle.mLastSector2 > 0.0
        ? vehicle.mLastSector2 - vehicle.mLastSector1 : -1.0;
      pti.lastS3Time = vehicle.mLastSector2 > 0.0 && vehicle.mLastLapTime > 0.0
        ? vehicle.mLastLapTime - vehicle.mLastSector2 : -1.0;

      pti.currS1Time = pti.lastS1Time;
      pti.currS2Time = pti.lastS2Time;
      pti.currS3Time = pti.lastS3Time;

      // Check if we have more current values for S1 and S2.
      // S3 always equals to lastS3Time.
      if (vehicle.mCurSector1 > 0.0)
        pti.currS1Time = vehicle.mCurSector1;

      if (vehicle.mCurSector1 > 0.0 && vehicle.mCurSector2 > 0.0)
        pti.currS2Time = vehicle.mCurSector2 - vehicle.mCurSector1;

      /*pti.bestS1Time = vehicle.mBestSector1 > 0.0 ? vehicle.mBestSector1 : -1.0;
      pti.bestS2Time = vehicle.mBestSector1 > 0.0 && vehicle.mBestSector2 > 0.0 ? vehicle.mBestSector2 - vehicle.mBestSector1 : -1.0;

      // This is not correct.  mBestLapTime does not neccessarily includes all three best sectors together.  The only way to calculate this is by continuous tracking.
      // However, currently there's no need for this value at all, so I don't care.
      pti.bestS3Time = vehicle.mBestSector2 > 0.0 && vehicle.mBestLapTime > 0.0 ? vehicle.mBestLapTime - vehicle.mBestSector2 : -1.0;*/

      // We need to skip previous player lap stats during comparison on new lap, hence we don't use vehicle values for those.
      pti.bestS1Time = bestLapStats.S1Time;
      pti.bestS2Time = bestLapStats.S2Time;
      pti.bestS3Time = bestLapStats.S3Time;
      pti.bestLapTime = bestLapStats.lapTime;

      pti.currLapET = vehicle.mLapStartET;
      pti.lastLapTime = vehicle.mLastLapTime;
      pti.currLapTime = scoring.mScoringInfo.mCurrentET - vehicle.mLapStartET;
      
      pti.currLap = vehicle.mTotalLaps;

      sbDetails = new StringBuilder();
      sbDetails.Append($"{name} {pti.name}\ncurrLapET: {this.lapTimeStr(pti.currLapET)}    lastLapTime: {this.lapTimeStr(pti.lastLapTime)}    currLapTime: {this.lapTimeStr(pti.currLapTime)}    bestLapTime: {this.lapTimeStr(pti.bestLapTime)}\n");
      sbDetails.Append($"lastS1: {this.lapTimeStr(pti.lastS1Time)}    lastS2: {this.lapTimeStr(pti.lastS2Time)}    lastS3: {this.lapTimeStr(pti.lastS3Time)}\n");
      sbDetails.Append($"currS1: {this.lapTimeStr(pti.currS1Time)}    currS2: {this.lapTimeStr(pti.currS2Time)}    currS3: {this.lapTimeStr(pti.currS3Time)}\n");
      sbDetails.Append($"bestS1: {this.lapTimeStr(pti.bestS1Time)}    bestS2: {this.lapTimeStr(pti.bestS2Time)}    bestS3: {this.lapTimeStr(pti.bestS3Time)}    bestTotal: {this.lapTimeStr(pti.bestS1Time + pti.bestS2Time + pti.bestS3Time)}\n");
    }
  }
}
