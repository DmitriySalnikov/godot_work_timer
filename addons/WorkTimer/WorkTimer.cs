//#define NO_DISCORD

#if TOOLS
using Godot;
using System;
using System.Text;

namespace WorkTimeCounter
{
    public abstract class WorkCounterAbstract
    {
        public TimeSpan TotalWorkTime { get; protected set; } = new TimeSpan();
        public TimeSpan CurrentWorkTime { get; protected set; } = new TimeSpan();
        public int TotalPlayTimes { get; protected set; } = 0;
        public int TotalExportTimes { get; protected set; } = 0;
        public DateTime FirstLaunch { get; protected set; } = DateTime.Now;

        public void AddTime(TimeSpan time)
        {
            TotalWorkTime += time;
            CurrentWorkTime += time;
        }
        public void IncrementPlays() => TotalPlayTimes++;
        public void IncrementExports() => TotalExportTimes++;
    }

    [Tool]
    public class WorkTimer : ToolButton
    {
        class WorkCounterData : WorkCounterAbstract
        {
            public enum TempDisabledState
            {
                Enabled,
                TimerStarted,
                CounterDisabled,
            }

            readonly string saveFile = "user://WorkTimer/save.json";
            readonly string saveFileBak = "user://WorkTimer/save.json.bak";
            readonly Node metaStorage = null;
            public int TempDisableDelayMinutes = 25;
            public bool IsCounterEnabled = true;
            public bool IsDiscordEnabled = true;
            public TempDisabledState IsCounterTempDisabled = TempDisabledState.Enabled;
            public DateTime TempDisableStartTime = DateTime.Now;

            public WorkCounterData()
            {
                metaStorage = WorkTimerPlugin.Instance;
                Load();
            }

            public void Save()
            {
                try
                {
                    metaStorage.SetMeta(nameof(CurrentWorkTime), CurrentWorkTime.Ticks.ToString());
                    metaStorage.SetMeta(nameof(IsCounterTempDisabled), ((int)IsCounterTempDisabled).ToString());
                    metaStorage.SetMeta(nameof(TempDisableStartTime), TempDisableStartTime.Ticks.ToString());

                    var file = new File();
                    var dir = new Directory();
                    if (file.FileExists(saveFile))
                    {
                        if (file.FileExists(saveFileBak))
                        {
                            if (dir.Remove(saveFileBak) != Error.Ok)
                            {
                                GD.PrintErr($"{LogPrefix}Can't remove backup file: {saveFileBak}");
                                return;
                            }
                        }
                        if (dir.Rename(saveFile, saveFileBak) != Error.Ok)
                        {
                            GD.PrintErr($"{LogPrefix}Can't rename backup file: {saveFileBak}");
                            return;
                        }
                    }
                    else
                    {
                        if (dir.MakeDirRecursive(saveFile.GetBaseDir()) != Error.Ok)
                        {
                            GD.PrintErr($"{LogPrefix}Can't make directory for save file: {saveFile.GetBaseDir()}");
                            return;
                        }
                    }

                    var dict = new Godot.Collections.Dictionary
                    {
                        // Store data
                        ["fist_launch"] = FirstLaunch.Ticks.ToString(),
                        ["time"] = TotalWorkTime.Ticks.ToString(),
                        ["plays"] = TotalPlayTimes.ToString(),
                        ["exports"] = TotalExportTimes.ToString(),
                        ["temp_disable_delay"] = TempDisableDelayMinutes.ToString(),
                        ["enabled"] = IsCounterEnabled.ToString(),
                        ["discord"] = IsDiscordEnabled.ToString(),
                    };

                    var text = JSON.Print(dict, "", true);
                    if (file.Open(saveFile, File.ModeFlags.Write) == Error.Ok)
                    {
                        file.StorePascalString(text);
                        file.Close();
                    }
                    else
                    {
                        GD.PrintErr($"{LogPrefix}Can't open save file for writing: {saveFile}");
                        return;
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"{LogPrefix}Exception when saving: {e.Message}\n{e.StackTrace}");
                    return;
                }
            }

            void Load()
            {
                try
                {
                    if (metaStorage.HasMeta(nameof(CurrentWorkTime)))
                        CurrentWorkTime = new TimeSpan(long.Parse((string)metaStorage.GetMeta(nameof(CurrentWorkTime))));

                    // Temporary disabling
                    if (metaStorage.HasMeta(nameof(IsCounterTempDisabled)))
                        IsCounterTempDisabled = (TempDisabledState)int.Parse((string)metaStorage.GetMeta(nameof(IsCounterTempDisabled)));
                    if (metaStorage.HasMeta(nameof(TempDisableStartTime)))
                        TempDisableStartTime = new DateTime(long.Parse((string)metaStorage.GetMeta(nameof(TempDisableStartTime))));

                    var file = new File();
                    if (file.FileExists(saveFile))
                    {
                        if (file.Open(saveFile, File.ModeFlags.Read) == Error.Ok)
                        {
                            var text = file.GetPascalString();
                            file.Close();

                            var res = JSON.Parse(text);
                            if (res.Error == Error.Ok && res.Result is Godot.Collections.Dictionary dict)
                            {
                                if (dict != null)
                                {
                                    string getValue(string key, string def)
                                    {
                                        if (dict.Contains(key))
                                            return (string)(dict[key]);
                                        return def;
                                    };
                                    // Loading data

                                    FirstLaunch = new DateTime(long.Parse(getValue("fist_launch", FirstLaunch.Ticks.ToString())));
                                    TotalWorkTime = new TimeSpan(long.Parse(getValue("time", TotalWorkTime.Ticks.ToString())));
                                    TotalPlayTimes = int.Parse(getValue("plays", TotalPlayTimes.ToString()));
                                    TotalExportTimes = int.Parse(getValue("exports", TotalExportTimes.ToString()));
                                    TempDisableDelayMinutes = int.Parse(getValue("temp_disable_delay", TempDisableDelayMinutes.ToString()));
                                    IsCounterEnabled = bool.Parse(getValue("enabled", IsCounterEnabled.ToString()));
                                    IsDiscordEnabled = bool.Parse(getValue("discord", IsDiscordEnabled.ToString()));
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"{LogPrefix}{e.Message}");
                }
            }
        }

        public WorkCounterAbstract Counter
        {
            get => workCounter;
        }

#if !NO_DISCORD
        Discord.Discord discord = null;
        CheckBox p_enabled_discord = null;
        DateTime discordPrevActiveTime = DateTime.Now;
#endif

        const string LogPrefix = "[WorkTimer] ";
        DateTime prevTime = DateTime.Now;
        bool isOneCreateErrorShowen = false;
        WorkCounterData workCounter = new WorkCounterData();

        Godot.Timer updateTimer = null;
        Godot.Timer saveTimer = null;
        Godot.Timer tempDisableTimer = null;
        PopupPanel popuPanel = null;

        Label p_first_launch_label = null;
        Label p_total_time_label = null;
        Label p_current_time_label = null;
        Label p_total_runs = null;
        Label p_total_exports = null;
        CheckBox p_enabled_box = null;
        SpinBox p_temp_disable_time = null;

        #region Init and Deinit everything

        public override void _EnterTree()
        {
            updateTimer = new Godot.Timer() { Name = "Update Timer", WaitTime = 10, Autostart = false };
            saveTimer = new Godot.Timer() { Name = "Save Timer", WaitTime = 30, Autostart = false };
            tempDisableTimer = new Godot.Timer() { Name = "Temp Disable Timer", WaitTime = 1, Autostart = false, OneShot = true };
            AddChild(updateTimer);
            AddChild(saveTimer);
            AddChild(tempDisableTimer);
            updateTimer.Start();
            saveTimer.Start();
            CreatePopupMenu();
            tempDisableTimer.Connect("timeout", this, nameof(TempDisableTimeout));
            updateTimer.Connect("timeout", this, nameof(UpdateTime));
            saveTimer.Connect("timeout", this, nameof(SaveData));

            ResetPrevTime();
            UpdateInfo();
#if !NO_DISCORD
            InitDiscord();
#endif

            if (workCounter.IsCounterTempDisabled == WorkCounterData.TempDisabledState.TimerStarted)
            {
                if ((workCounter.TempDisableDelayMinutes * 60) - (float)(DateTime.Now - workCounter.TempDisableStartTime).TotalSeconds > 0)
                {
                    tempDisableTimer.WaitTime = (workCounter.TempDisableDelayMinutes * 60) - (float)(DateTime.Now - workCounter.TempDisableStartTime).TotalSeconds;
                    tempDisableTimer.Start();
                }
                else
                {
                    TempDisableTimeout();
                }
            }
            else if (workCounter.IsCounterTempDisabled == WorkCounterData.TempDisabledState.Enabled)
                SetupTimerConnections(workCounter.IsCounterEnabled);
            else
                SetupTimerConnections(false);
        }

        public override void _ExitTree()
        {
            _Deinit();
            updateTimer?.QueueFree();
            saveTimer?.QueueFree();
            popuPanel?.QueueFree();
        }

        protected override void Dispose(bool disposing)
        {
            _Deinit();
            base.Dispose(disposing);
        }

        void _Deinit()
        {
#if !NO_DISCORD
            if (discord != null)
            {
                discord.GetActivityManager().ClearActivity(result => { });
                discord.Dispose();
                GD.Print($"{LogPrefix}Discord plugin stopped.");
            }
            discord = null;
#endif

            workCounter?.Save();
            workCounter = null;
        }

        #endregion Init and Deinit everything

        public override void _Notification(int what)
        {
            switch (what)
            {
                case NotificationWmFocusIn:
                    {
                        tempDisableTimer.Stop();
                        if (workCounter.IsCounterTempDisabled != WorkCounterData.TempDisabledState.Enabled)
                        {
                            workCounter.IsCounterTempDisabled = WorkCounterData.TempDisabledState.Enabled;
                            if (workCounter.IsCounterEnabled)
                            {
                                ResetPrevTime();
                                SetupTimerConnections(true);
                            }
                        }
                        break;
                    }
                case NotificationWmFocusOut:
                    {
                        if (workCounter.TempDisableDelayMinutes > 0 && workCounter.IsCounterEnabled)
                        {
                            workCounter.IsCounterTempDisabled = WorkCounterData.TempDisabledState.TimerStarted;
                            workCounter.TempDisableStartTime = DateTime.Now;
                            tempDisableTimer.WaitTime = workCounter.TempDisableDelayMinutes * 60;
                            tempDisableTimer.Start();
                        }
                        break;
                    }
            }
        }

        void TempDisableTimeout()
        {
            GD.Print($"{LogPrefix}Time counter temporary disabled");
            workCounter.IsCounterTempDisabled = WorkCounterData.TempDisabledState.CounterDisabled;
            SetupTimerConnections(false);
        }

        void SaveData()
        {
            workCounter.Save();
        }

        void CreatePopupMenu()
        {
            popuPanel = new PopupPanel()
            {
                Name = "WorkTimer PopupMenu",
            };
            AddChild(popuPanel);

            var vbox = new VBoxContainer();
            popuPanel.AddChild(vbox);

            p_total_time_label = new Label() { Name = "TotalTimeLabel" };
            p_current_time_label = new Label() { Name = "CurrnetTimeLabel" };
            p_total_runs = new Label() { Name = "TotalRunsLabel" };
            p_total_exports = new Label() { Name = "TotalExportsLabel" };
            p_first_launch_label = new Label() { Name = "FirstLaunchLabel" };
            p_enabled_box = new CheckBox() { Text = "Time Counter Enabled" };
#if !NO_DISCORD
            p_enabled_discord = new CheckBox() { Text = "Discord Plugin Enabled" };
#endif
            p_temp_disable_time = new SpinBox() { Suffix = "min", Rounded = true, MaxValue = 24 * 60 };
            var temp_tdt_line = new HBoxContainer();
            var temp_tdt_label = new Label() { Text = "Time counter auto disable delay" };

            vbox.AddChild(p_total_time_label);
            vbox.AddChild(p_current_time_label);
            vbox.AddChild(p_total_runs);
            vbox.AddChild(p_total_exports);
            vbox.AddChild(p_first_launch_label);

#if !NO_DISCORD
            vbox.AddChild(p_enabled_discord);
#endif
            vbox.AddChild(p_enabled_box);
            vbox.AddChild(temp_tdt_line);
            temp_tdt_line.AddChild(p_temp_disable_time);
            temp_tdt_line.AddChild(temp_tdt_label);

            p_enabled_box.Connect("toggled", this, nameof(EnabledToggled));
            p_enabled_box.Pressed = workCounter.IsCounterEnabled;

#if !NO_DISCORD
            p_enabled_discord.Connect("toggled", this, nameof(DiscordToggled));
            p_enabled_discord.Pressed = workCounter.IsDiscordEnabled;
#endif

            p_temp_disable_time.Connect("value_changed", this, nameof(TempDisableTimeValueChanged));
            p_temp_disable_time.Value = workCounter.TempDisableDelayMinutes;
        }

        void TempDisableTimeValueChanged(float value)
        {
            workCounter.TempDisableDelayMinutes = (int)value;
        }

        void EnabledToggled(bool enabled)
        {
            SetupTimerConnections(enabled);
            workCounter.IsCounterEnabled = enabled;
        }

#if !NO_DISCORD
        void DiscordToggled(bool enabled)
        {
            // SetupTimerConnections(enabled);
            workCounter.IsDiscordEnabled = enabled;
        }
#endif

        void SetupTimerConnections(bool enabled)
        {
            if (enabled)
            {
                updateTimer.Start();
                saveTimer.Start();
            }
            else
            {
                updateTimer.Stop();
                saveTimer.Stop();
            }
        }

        public void UpdateTime()
        {
            workCounter.AddTime(DateTime.Now - prevTime);
            ResetPrevTime();
            UpdateInfo();
        }

        void UpdateInfo()
        {
            Text = GetTimeString(workCounter.TotalWorkTime);
            UpdatePopup();
        }

        void UpdatePopup()
        {
            p_total_time_label.Text = $"Total work time: {GetTimeString(workCounter.TotalWorkTime, true)} ({(int)workCounter.TotalWorkTime.TotalHours}h.)";
            p_current_time_label.Text = $"Current session work time: {GetTimeString(workCounter.CurrentWorkTime, true)}";
            p_total_runs.Text = $"Number of Plays: {workCounter.TotalPlayTimes}";
            p_total_exports.Text = $"Number of Exports: {workCounter.TotalExportTimes}";
            p_first_launch_label.Text = $"First Launch Date: {workCounter.FirstLaunch:D}";
        }

        void ResetPrevTime()
        {
            prevTime = DateTime.Now;
        }

        public override void _Pressed()
        {
            popuPanel.RectGlobalPosition = RectGlobalPosition + RectSize;
            popuPanel?.Popup_();
            base._Pressed();
        }

        public override void _PhysicsProcess(float delta)
        {
#if !NO_DISCORD
            if (discord != null)
            {
                try
                {
                    UpdateActivity();
                    discord.RunCallbacks();
                    discordPrevActiveTime = DateTime.Now;
                    return;
                }
                catch (Discord.ResultException e)
                {
                    GD.PrintErr($"{LogPrefix}Discord exception: {e.Message}");
                    discord.Dispose();
                    discord = null;
                }
                finally
                {
                    if (!workCounter.IsDiscordEnabled && discord != null)
                    {
                        discord.Dispose();
                        discord = null;
                        GD.Print($"{LogPrefix}Discord plugin stopped.");
                    }
                }
            }

            if (workCounter.IsDiscordEnabled && (DateTime.Now - discordPrevActiveTime).TotalSeconds > 1.0f)
            {
                discordPrevActiveTime = DateTime.Now;
                InitDiscord();
            }
#endif
        }

        static string ConvertStrToUTF8(string str)
        {
            return Encoding.UTF8.GetString(Encoding.Default.GetBytes(str));
        }

        static string GetTimeString(TimeSpan time, bool full = false)
        {
            if (full)
                return $"{(int)time.TotalDays}d. {(int)time.Hours}h. {(int)time.Minutes:00}m.";

            if ((int)time.TotalDays == 0)
                return $"{(int)time.Hours}h. {(int)time.Minutes:00}m.";
            return $"{(int)time.TotalDays}d. {(int)time.Hours:00}h.";
        }

#if !NO_DISCORD
        void InitDiscord()
        {
            try
            {
                discord = new Discord.Discord(811720037064769566, (UInt64)Discord.CreateFlags.NoRequireDiscord);
                GD.Print($"{LogPrefix}Discord plugin started!");
            }
            catch (Exception e)
            {
                if (!isOneCreateErrorShowen)
                {
                    GD.PrintErr($"{LogPrefix}Can't start the Discord plugin. Exception message: {e.Message}. Maybe your Discord isn't running.");
                    isOneCreateErrorShowen = true;
                }
                return;
            }
            isOneCreateErrorShowen = false;

            discord.SetLogHook(Discord.LogLevel.Debug, (level, message) =>
            {
                GD.Print($"Log[{level}] {message}");
            });
        }
#endif

        // Request user's avatar data. Sizes can be powers of 2 between 16 and 2048
#if !NO_DISCORD
        static void FetchAvatar(Discord.ImageManager imageManager, Int64 userID, Action<Int64, ImageTexture> onCompleted)
        {
            imageManager.Fetch(Discord.ImageHandle.User(userID), (result, handle) =>
            {
                {
                    ImageTexture imgT = null;
                    if (result == Discord.Result.Ok)
                    {
                        // These return raw RGBA.
                        var data = imageManager.GetData(handle);
                        var dims = imageManager.GetDimensions(handle);
                        imgT = new ImageTexture();
                        var imgI = new Image();
                        imgI.CreateFromData((int)dims.Width, (int)dims.Height, false, Image.Format.Rgba8, data);
                        imgT.CreateFromImage(imgI);
                    }
                    else
                    {
                        GD.PrintErr($"{LogPrefix}Discord load image error {handle.Id}");
                    }

                    onCompleted?.Invoke(userID, imgT);
                }
            });
        }

        // Update user's activity for your game.
        // Party and secrets are vital.
        // Read https://discordapp.com/developers/docs/rich-presence/how-to for more details.
        void UpdateActivity()
        {
            var activityManager = discord.GetActivityManager();
            var applicationManager = discord.GetApplicationManager();

            if (workCounter.IsCounterEnabled)
            {
                var st = (Engine.GetMainLoop() as SceneTree);
                var afk = workCounter.IsCounterTempDisabled == WorkCounterData.TempDisabledState.CounterDisabled ? $" AFK( {GetTimeString(DateTime.Now - workCounter.TempDisableStartTime)} )" : "";
                var state = ConvertStrToUTF8($"Work {GetTimeString(workCounter.TotalWorkTime)}{afk}");

                var activity = new Discord.Activity
                {
                    Details = ConvertStrToUTF8($"Project: {(string)ProjectSettings.GetSetting("application/config/name")}"),
                    State = state,
                    Instance = true,
                };
                if (!afk.Empty())
                    activity.Assets = new Discord.ActivityAssets
                    {
                        LargeImage = "godot_pepe",
                        SmallImage = "afk_smoll",
                    };
                else
                    activity.Assets = new Discord.ActivityAssets
                    {
                        LargeImage = "godot_pepe",
                    };

                activityManager.UpdateActivity(activity, result => { });
            }
            else
            {
                var activity = new Discord.Activity
                {
                    Details = ConvertStrToUTF8($"Project: {(string)ProjectSettings.GetSetting("application/config/name")}"),
                    State = "Time counter disabled.",
                    Instance = true,
                    Assets =
                    {
                        LargeImage = "godot_pepe",
                    },
                };
                activityManager.UpdateActivity(activity, result => { });
            }

        }
#endif
    }
}
#endif
