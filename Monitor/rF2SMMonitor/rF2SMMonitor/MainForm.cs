﻿/*
rF2SMMonitor is visual debugger for rF2 Shared Memory Plugin.

MainForm implementation, contains main loop and render calls.

Author: The Iron Wolf (vleonavicius @hotmail.com)
*/
using rF2SMMonitor.rFactor2Data;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static rF2SMMonitor.rFactor2Constants;

namespace rF2SMMonitor
{
  public partial class MainForm : Form
  {
    // Connection fields
    private const int CONNECTION_RETRY_INTERVAL_MS = 1000;
    private const int DISCONNECTED_CHECK_INTERVAL_MS = 15000;
    private const float DEGREES_IN_RADIAN = 57.2957795f;
    System.Windows.Forms.Timer connectTimer = new System.Windows.Forms.Timer();
    System.Windows.Forms.Timer disconnectTimer = new System.Windows.Forms.Timer();
    bool connected = false;

    // Shared memory buffer fields
    readonly int SHARED_MEMORY_SIZE_BYTES = Marshal.SizeOf(typeof(rF2State));
    readonly int SHARED_MEMORY_HEADER_SIZE_BYTES = Marshal.SizeOf(typeof(rF2StateHeader));
    byte[] sharedMemoryReadBuffer = null;

    // Plugin access fields
    Mutex fileAccessMutex = null;
    MemoryMappedFile memoryMappedFile1 = null;
    MemoryMappedFile memoryMappedFile2 = null;
    int currBuff = 0;

    // Marshalled view
    rF2State currrF2State;

    // Interpolation debugging
    InterpolationStats interpolationStats = new InterpolationStats();

    // Config
    IniFile config = new IniFile();
    float scale = 2.0f;
    float xOffset = 0.0f;
    float yOffset = 0.0f;
    int focusVehicle = 0;
    bool centerOnVehicle = true;
    bool rotateAroundVehicle = true;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
      public IntPtr Handle;
      public uint Message;
      public IntPtr WParameter;
      public IntPtr LParameter;
      public uint Time;
      public Point Location;
    }

    [DllImport("user32.dll")]
    public static extern int PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    public MainForm()
    {
      this.InitializeComponent();

      this.DoubleBuffered = true;
      this.StartPosition = FormStartPosition.Manual;
      this.Location = new Point(0, 0);

      this.EnableControls(false);
      this.scaleTextBox.KeyDown += TextBox_KeyDown;
      this.scaleTextBox.LostFocus += ScaleTextBox_LostFocus;
      this.xOffsetTextBox.KeyDown += TextBox_KeyDown;
      this.xOffsetTextBox.LostFocus += XOffsetTextBox_LostFocus;
      this.yOffsetTextBox.KeyDown += TextBox_KeyDown;
      this.yOffsetTextBox.LostFocus += YOffsetTextBox_LostFocus;
      this.focusVehTextBox.KeyDown += TextBox_KeyDown;
      this.focusVehTextBox.LostFocus += FocusVehTextBox_LostFocus;
      this.setAsOriginCheckBox.CheckedChanged += SetAsOriginCheckBox_CheckedChanged;
      this.rotateAroundCheckBox.CheckedChanged += RotateAroundCheckBox_CheckedChanged;
      this.MouseWheel += MainForm_MouseWheel;

      this.LoadConfig();
      this.connectTimer.Interval = CONNECTION_RETRY_INTERVAL_MS;
      this.connectTimer.Tick += ConnectTimer_Tick;
      this.disconnectTimer.Interval = DISCONNECTED_CHECK_INTERVAL_MS;
      this.disconnectTimer.Tick += DisconnectTimer_Tick;
      this.connectTimer.Start();
      this.disconnectTimer.Start();

      this.view.BorderStyle = BorderStyle.Fixed3D;
      this.view.Paint += View_Paint;
      this.MouseClick += MainForm_MouseClick;
      this.view.MouseClick += MainForm_MouseClick;

      Application.Idle += HandleApplicationIdle;
    }

    private void MainForm_MouseClick(object sender, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right)
        this.interpolationStats.Reset();
    }

    private void YOffsetTextBox_LostFocus(object sender, EventArgs e)
    {
      float result = 0.0f;
      if (float.TryParse(this.yOffsetTextBox.Text, out result))
      {
        this.yOffset = result;
        this.config.Write("yOffset", this.yOffset.ToString());
      }
      else
        this.yOffsetTextBox.Text = this.yOffset.ToString();

    }

    private void XOffsetTextBox_LostFocus(object sender, EventArgs e)
    {
      float result = 0.0f;
      if (float.TryParse(this.xOffsetTextBox.Text, out result))
      {
        this.xOffset = result;
        this.config.Write("xOffset", this.xOffset.ToString());
      }
      else
        this.xOffsetTextBox.Text = this.xOffset.ToString();

    }

    private void MainForm_MouseWheel(object sender, MouseEventArgs e)
    {
      float step = 0.5f;
      if (this.scale < 5.0f)
        step = 0.25f;
      else if (this.scale < 2.0f)
        step = 0.1f;
      else if (this.scale < 1.0f)
        step = 0.05f;

      if (e.Delta > 0)
        this.scale += step;
      else if (e.Delta < 0)
        this.scale -= step;

      if (this.scale <= 0.0f)
        this.scale = 0.05f;

      this.config.Write("scale", this.scale.ToString());
      this.scaleTextBox.Text = this.scale.ToString();
    }

    private void RotateAroundCheckBox_CheckedChanged(object sender, EventArgs e)
    {
      this.rotateAroundVehicle = this.rotateAroundCheckBox.Checked;
      this.config.Write("rotateAroundVehicle", this.rotateAroundVehicle ? "1" : "0");
    }

    private void SetAsOriginCheckBox_CheckedChanged(object sender, EventArgs e)
    {
      this.centerOnVehicle = this.setAsOriginCheckBox.Checked;
      this.rotateAroundCheckBox.Enabled = this.setAsOriginCheckBox.Checked;
      this.config.Write("centerOnVehicle", this.centerOnVehicle ? "1" : "0");
    }

    private void FocusVehTextBox_LostFocus(object sender, EventArgs e)
    {
      int result = 0;
      if (int.TryParse(this.focusVehTextBox.Text, out result) && result >= 0)
      {
        this.focusVehicle = result;
        this.config.Write("focusVehicle", this.focusVehicle.ToString());
      }
      else
        this.focusVehTextBox.Text = this.focusVehTextBox.ToString();
    }

    private void ScaleTextBox_LostFocus(object sender, EventArgs e)
    {
      float result = 0.0f;
      if (float.TryParse(this.scaleTextBox.Text, out result))
      {
        this.scale = result;
        this.config.Write("scale", this.scale.ToString());
      }
      else
        this.scaleTextBox.Text = this.scale.ToString();
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyCode == Keys.Enter)
        this.view.Focus();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
        components.Dispose();
      
      if (disposing)
        Disconnect();

      base.Dispose(disposing);
    }

    // Amazing loop implementation by Josh Petrie from:
    // http://gamedev.stackexchange.com/questions/67651/what-is-the-standard-c-windows-forms-game-loop
    bool IsApplicationIdle()
    {
      NativeMessage result;
      return PeekMessage(out result, IntPtr.Zero, (uint)0, (uint)0, (uint)0) == 0;
    }

    void HandleApplicationIdle(object sender, EventArgs e)
    {
      while (this.IsApplicationIdle())
      {
        this.MainUpdate();
        this.MainRender();
      }
    }

    void MainUpdate()
    {
      if (!this.connected)
        return;

      try
      {
        // Note: if it is critical for client minimize wait time, same strategy as plugin uses can be employed.
        // Pass 0 timeout and skip update if someone holds the lock.
        if (this.fileAccessMutex.WaitOne(5000))
        {
          try
          {
            bool buf1Current = false;
            // Try buffer 1:
            using (var sharedMemoryStreamView = this.memoryMappedFile1.CreateViewStream())
            {
              var sharedMemoryStream = new BinaryReader(sharedMemoryStreamView);
              this.sharedMemoryReadBuffer = sharedMemoryStream.ReadBytes(this.SHARED_MEMORY_HEADER_SIZE_BYTES);

              // Marhsal header
              var headerHandle = GCHandle.Alloc(this.sharedMemoryReadBuffer, GCHandleType.Pinned);
              var header = (rF2StateHeader)Marshal.PtrToStructure(headerHandle.AddrOfPinnedObject(), typeof(rF2StateHeader));
              headerHandle.Free();

              if (header.mCurrentRead == 1)
              {
                sharedMemoryStream.BaseStream.Position = 0;
                this.sharedMemoryReadBuffer = sharedMemoryStream.ReadBytes(this.SHARED_MEMORY_SIZE_BYTES);
                buf1Current = true;
                this.currBuff = 1;
              }
            }

            // Read buffer 2
            if (!buf1Current)
            {
              using (var sharedMemoryStreamView = this.memoryMappedFile2.CreateViewStream())
              {
                var sharedMemoryStream = new BinaryReader(sharedMemoryStreamView);
                this.sharedMemoryReadBuffer = sharedMemoryStream.ReadBytes(this.SHARED_MEMORY_SIZE_BYTES);
                this.currBuff = 2;
              }
            }
          }
          finally
          {
            this.fileAccessMutex.ReleaseMutex();
          }

          // Marshal rF2State
          var handle = GCHandle.Alloc(this.sharedMemoryReadBuffer, GCHandleType.Pinned);
          this.currrF2State = (rF2State)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(rF2State));
          handle.Free();
        }
      }
      catch (Exception)
      {
        this.Disconnect();
      }
    }

    void MainRender()
    {
      this.view.Refresh();
    }

    int framesAvg = 20;
    int frame = 0;
    int fps = 0;
    Stopwatch fpsStopWatch = new Stopwatch();
    private void UpdateFPS()
    {
      if (this.frame > this.framesAvg)
      {
        this.fpsStopWatch.Stop();
        var tsSinceLastRender = this.fpsStopWatch.Elapsed;
        this.fps = tsSinceLastRender.Milliseconds > 0 ? (1000 * this.framesAvg) / tsSinceLastRender.Milliseconds : 0;
        this.fpsStopWatch.Restart();
        this.frame = 0;
      }
      else
        ++this.frame;
    }

    // Corrdinate conversion:
    // rF2 +x = screen +x
    // rF2 +z = screen -z
    // rF2 +yaw = screen -yaw
    // If I don't flip z, the projection will look from below.
    void View_Paint(object sender, PaintEventArgs e)
    {
      this.UpdateFPS();

      var g = e.Graphics;
      if (!this.connected)
      {
        var brush = new SolidBrush(System.Drawing.Color.Black);
        g.DrawString("Not connected", SystemFonts.DefaultFont, brush, 3.0f, 3.0f);
      }
      else
      {
        var brush = new SolidBrush(System.Drawing.Color.Green);

        var currX = 3.0f;
        var currY = 3.0f;
        float yStep = SystemFonts.DefaultFont.Height;
        g.DrawString(string.Format("FPS: {0}", this.fps), SystemFonts.DefaultFont, brush, currX, currY);
        g.DrawString(string.Format("mDetlaTime: {0:n4}", this.currrF2State.mDeltaTime), SystemFonts.DefaultFont, brush, currX, currY += yStep);
        g.DrawString(string.Format("mPos: x = {0:n3} y = {1:n3} z = {2:n3}", this.currrF2State.mPos.x, this.currrF2State.mPos.y, this.currrF2State.mPos.z), SystemFonts.DefaultFont, brush, currX, currY += yStep);

        Interpolator.RenderDebugInfo(ref this.currrF2State, g);

        this.interpolationStats.RenderInterpolationInfo(ref this.currrF2State, g, this.currBuff);

        // Branch of UI choice: origin center or car# center
        // Fix rotation on car of choice or no.
        // Draw axes
        // Scale will be parameter, scale applied last on render to zoom.
        float scale = this.scale;

        var xVeh = (float)this.currrF2State.mPos.x;
        var zVeh = (float)this.currrF2State.mPos.z;
        var yawVeh = Math.Atan2(this.currrF2State.mOri[2].x, this.currrF2State.mOri[2].z);

        g.DrawString(string.Format("yaw: {0:n3}", yawVeh), SystemFonts.DefaultFont, brush, currX, currY += yStep);

        // View center
        var xScrOrigin = this.view.Width / 2.0f;
        var yScrOrigin = this.view.Height / 2.0f;
        if (!this.centerOnVehicle)
        {
          // Set world origin.
          g.TranslateTransform(xScrOrigin, yScrOrigin);
          this.RenderOrientationAxis(g);
          g.ScaleTransform(scale, scale);

          RenderCar(g, xVeh, -zVeh, -(float)yawVeh, Brushes.Green);

          for (int i = 0; i < this.currrF2State.mNumVehicles; ++i)
            this.RenderCar(g,
              (float)this.currrF2State.mVehicles[i].mPos.x,
              -(float)this.currrF2State.mVehicles[i].mPos.z,
              -(float)this.currrF2State.mVehicles[i].mYaw, Brushes.Red);
        }
        else
        {
          g.TranslateTransform(xScrOrigin, yScrOrigin);

          if (this.rotateAroundVehicle)
            g.RotateTransform(180.0f + (float)yawVeh * DEGREES_IN_RADIAN);

          this.RenderOrientationAxis(g);
          g.ScaleTransform(scale, scale);
          g.TranslateTransform(-xVeh, zVeh);

          RenderCar(g, xVeh, -zVeh, -(float)yawVeh, Brushes.Green);

          for (int i = 0; i < this.currrF2State.mNumVehicles; ++i)
            this.RenderCar(g,
              (float)this.currrF2State.mVehicles[i].mPos.x,
              -(float)this.currrF2State.mVehicles[i].mPos.z,
             -(float)this.currrF2State.mVehicles[i].mYaw, Brushes.Red);


          //          RenderCar(g, (float)Interpolator.scoringX, (float)-Interpolator.scoringZ, -(float)Interpolator.scoringYaw, Brushes.Black);
          //RenderCar(g, (float)Interpolator.nlerpX, (float)-Interpolator.nlerpZ, -(float)Interpolator.nlerpYaw, Brushes.Orange);
 //         RenderCar(g, (float)Interpolator.matX, (float)-Interpolator.matZ, -(float)Interpolator.matYaw, Brushes.Blue);
        }

      }
    }


    // Length
    // 174.6in (4,435mm)
    // 175.6in (4,460mm) (Z06, ZR1)
    // Width
    // 72.6in (1,844mm)
    // 75.9in (1,928mm) (Z06, ZR1)
    /*PointF[] carPoly =
    {
        new PointF(0.922f, 2.217f),
        new PointF(0.922f, -1.4f),
        new PointF(1.3f, -1.4f),
        new PointF(0.0f, -2.217f),
        new PointF(-1.3f, -1.4f),
        new PointF(-0.922f, -1.4f),
        new PointF(-0.922f, 2.217f),
      };*/

    PointF[] carPoly =
    {
      new PointF(-0.922f, -2.217f),
      new PointF(-0.922f, 1.4f),
      new PointF(-1.3f, 1.4f),
      new PointF(0.0f, 2.217f),
      new PointF(1.3f, 1.4f),
      new PointF(0.922f, 1.4f),
      new PointF(0.922f, -2.217f),
    };

    private void RenderCar(Graphics g, float x, float y, float yaw, Brush brush)
    {
      var state = g.Save();

      g.TranslateTransform(x, y);

      g.RotateTransform(yaw * DEGREES_IN_RADIAN);

      g.FillPolygon(brush, this.carPoly);

      g.Restore(state);
    }

    static float arrowSide = 10.0f;
    PointF[] arrowHead =
    {
      new PointF(-arrowSide / 2.0f, -arrowSide / 2.0f),
      new PointF(0.0f, arrowSide / 2.0f),
      new PointF(arrowSide / 2.0f, -arrowSide / 2.0f)
    };

    private void RenderOrientationAxis(Graphics g)
    {

      float length = 1000.0f;
      float arrowDistX = this.view.Width / 2.0f - 10.0f;
      float arrowDistY = this.view.Height / 2.0f - 10.0f;

      // X (x screen) axis
      g.DrawLine(Pens.Red, -length, 0.0f, length, 0.0f);
      var state = g.Save();
      g.TranslateTransform(this.rotateAroundVehicle ? arrowDistY : arrowDistX, 0.0f);
      g.RotateTransform(-90.0f);
      g.FillPolygon(Brushes.Red, this.arrowHead);
      g.RotateTransform(90.0f);
      g.DrawString("x+", SystemFonts.DefaultFont, Brushes.Red, -10.0f, 10.0f);
      g.Restore(state);

      state = g.Save();
      // Z (y screen) axis
      g.DrawLine(Pens.Blue, 0.0f, -length, 0.0f, length);
      g.TranslateTransform(0.0f, -arrowDistY);
      g.RotateTransform(180.0f);
      g.FillPolygon(Brushes.Blue, this.arrowHead);
      g.DrawString("z+", SystemFonts.DefaultFont, Brushes.Blue, 10.0f, -10.0f);

      g.Restore(state);
    }

    private void ConnectTimer_Tick(object sender, EventArgs e)
    {
      if (!this.connected)
      {
        try
        {
          this.fileAccessMutex = Mutex.OpenExisting(rF2SMMonitor.rFactor2Constants.MM_FILE_ACCESS_MUTEX);
          this.memoryMappedFile1 = MemoryMappedFile.OpenExisting(rFactor2Constants.MM_FILE_NAME1);
          this.memoryMappedFile2 = MemoryMappedFile.OpenExisting(rFactor2Constants.MM_FILE_NAME2);
          // NOTE: Make sure that SHARED_MEMORY_SIZE_BYTES matches the structure size in the plugin (debug mode prints).
          this.sharedMemoryReadBuffer = new byte[this.SHARED_MEMORY_SIZE_BYTES];
          this.connected = true;

          this.EnableControls(true);
        }
        catch (Exception)
        {
          Disconnect();
        }
      }
    }

    private void DisconnectTimer_Tick(object sender, EventArgs e)
    {
      if (!this.connected)
        return;

      try
      {
        // Alternatively, I could release resources and try re-acquiring them immidiately.
        var processes = Process.GetProcessesByName(rF2SMMonitor.rFactor2Constants.RFACTOR2_PROCESS_NAME);
        if (processes.Length == 0)
          Disconnect();
      }
      catch (Exception)
      {
        Disconnect();
      }
    }
    private void Disconnect()
    {
      if (this.memoryMappedFile1 != null)
        this.memoryMappedFile1.Dispose();

      if (this.memoryMappedFile2 != null)
        this.memoryMappedFile2.Dispose();

      if (this.fileAccessMutex != null)
        this.fileAccessMutex.Dispose();

      this.memoryMappedFile1 = null;
      this.memoryMappedFile2 = null;
      this.sharedMemoryReadBuffer = null;
      this.connected = false;
      this.fileAccessMutex = null;

      this.EnableControls(false);
    }

    void EnableControls(bool enable)
    {
      this.globalGroupBox.Enabled = enable;
      this.groupBoxFocus.Enabled = enable;

      this.focusVehLabel.Enabled = false;
      this.focusVehTextBox.Enabled = false;
      this.xOffsetLabel.Enabled = false;
      this.xOffsetTextBox.Enabled = false;
      this.yOffsetLabel.Enabled = false;
      this.yOffsetTextBox.Enabled = false;

      if (enable)
        this.rotateAroundCheckBox.Enabled = this.setAsOriginCheckBox.Checked;
    }

    void LoadConfig()
    {
      float result = 0.0f;
      this.scale = 2.0f;
      if (float.TryParse(this.config.Read("scale"), out result))
        this.scale = result;

      if (this.scale <= 0.0f)
        this.scale = 0.1f;

      this.scaleTextBox.Text = this.scale.ToString();

      result = 0.0f;
      this.xOffset = 0.0f;
      if (float.TryParse(this.config.Read("xOffset"), out result))
        this.xOffset = result;

      this.xOffsetTextBox.Text = this.xOffset.ToString();

      result = 0.0f;
      this.yOffset = 0.0f;
      if (float.TryParse(this.config.Read("yOffset"), out result))
        this.yOffset = result;

      this.yOffsetTextBox.Text = this.yOffset.ToString();

      int intResult = 0;
      this.focusVehicle = 0;
      if (int.TryParse(this.config.Read("focusVehicle"), out intResult) && intResult >= 0)
        this.focusVehicle = intResult;

      this.focusVehTextBox.Text = this.focusVehicle.ToString();

      intResult = 0;
      this.centerOnVehicle = true;
      if (int.TryParse(this.config.Read("centerOnVehicle"), out intResult) && intResult == 0)
        this.centerOnVehicle = false;

      this.setAsOriginCheckBox.Checked = this.centerOnVehicle;

      intResult = 0;
      this.rotateAroundVehicle = true;
      if (int.TryParse(this.config.Read("rotateAroundVehicle"), out intResult) && intResult == 0)
        this.rotateAroundVehicle = false;

      this.rotateAroundCheckBox.Checked = this.rotateAroundVehicle;
    }
  }
}