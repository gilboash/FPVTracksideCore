﻿using Composition.Nodes;
using ExternalData;
using OBSWebsocketDotNet.Types;
using RaceLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Timing;
using Tools;
using UI.Nodes;

namespace UI
{
    public class OBSRemoteControlManager : IDisposable
    {
        public enum Triggers
        {

            ClickStartRace,
            StartRaceTone,
            RaceEnd,
            TimesUp,

            RaceStartCancelled,
           
            PreRaceScene,
            PostRaceScene,
            LiveScene,


            LiveTab,
            RoundsTab,
            ReplayTab,

            LapRecordsTab,
            LapCountTab,
            PointsTab,
            ChannelListTab,
            RSSITab,

            PhotoBoothTab,
            PatreonsTab,

            ChannelGrid1,
            ChannelGrid2,
            ChannelGrid3,
            ChannelGrid4,
            ChannelGrid5,
            ChannelGrid6,
            ChannelGrid7,
            ChannelGrid8,

            EditLaps,

            LapDetection,
            Sector1Detection,
            Sector2Detection,
            Sector3Detection,
            Sector4Detection,
            Sector5Detection,
            Sector6Detection,
            Sector7Detection,
            Sector8Detection,
            Sector9Detection,
            Sector10Detection,
        }

        private OBSRemoteControlConfig config;
        private OBSRemoteControl remoteControl;

        private SceneManagerNode sceneManagerNode;
        private TracksideTabbedMultiNode tabbedMultiNode;
        private EventManager eventManager;

        private bool eventsHooked;

        public event Action<bool> Activity;

        public bool Connected 
        { 
            get
            {
                if (remoteControl == null)
                    return false;
                return remoteControl.Connected;
            }
        }

        public bool Enabled
        {
            get
            {
                return config.Enabled;
            }
        }

        public bool Active { get; set; }

        public IEnumerable<Triggers> ChannelGrids
        {
            get
            {
                yield return Triggers.ChannelGrid1;
                yield return Triggers.ChannelGrid2;
                yield return Triggers.ChannelGrid3;
                yield return Triggers.ChannelGrid4;
                yield return Triggers.ChannelGrid5;
                yield return Triggers.ChannelGrid6;
                yield return Triggers.ChannelGrid7;
                yield return Triggers.ChannelGrid8;
            }
        }

        private TimeSpan doubleTriggerTimeout;

        private Triggers lastTrigger;
        private DateTime lastTriggerTime;

        private int sectorCounter;


        public OBSRemoteControlManager(SceneManagerNode sceneManagerNode, TracksideTabbedMultiNode tabbedMultiNode, EventManager eventManager)
        {
            doubleTriggerTimeout = TimeSpan.FromSeconds(5);

            this.sceneManagerNode = sceneManagerNode;
            this.tabbedMultiNode = tabbedMultiNode;
            this.eventManager = eventManager;

            Active = true;

            config = OBSRemoteControlConfig.Load(eventManager.Profile);

            if (config.Enabled) 
            {
                sectorCounter = 0;

                sceneManagerNode.OnSceneChange += OnSceneChange;
                eventManager.RaceManager.OnRaceStart += OnRaceStart;
                eventManager.RaceManager.OnRacePreStart += OnRacePreStart;
                eventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;
                eventManager.RaceManager.OnRaceCancelled += RaceManager_OnRaceCancelled;
                eventManager.RaceManager.OnRaceResumed += OnRaceStart;
                tabbedMultiNode.OnTabChange += OnTabChange;
                sceneManagerNode.ChannelsGridNode.OnGridCountChanged += OnGridCountChanged;

                eventManager.RaceManager.OnLapDetected += OnLap;
                eventManager.RaceManager.OnSplitDetection += OnDetection;

                eventsHooked = true;

                remoteControl = new OBSRemoteControl(config.Host, config.Port, config.Password);
                remoteControl.Activity += RemoteControl_Activity;
                remoteControl.Connect();
            }
        }

        private void OnDetection(Detection detection)
        {
            if (detection == null)
                return;

            int sector = detection.RaceSector;

            if (sector > sectorCounter)
            {
                Triggers t = Triggers.LapDetection + detection.TimingSystemIndex;
                Trigger(t);
                sectorCounter = sector;
            }
        }

        private void OnLap(Lap lap)
        {
            if (lap != null)
            {
                OnDetection(lap.Detection);
            }
        }

        public void Dispose()
        {
            remoteControl?.Dispose();

            if (eventsHooked)
            {
                sceneManagerNode.OnSceneChange -= OnSceneChange;
                eventManager.RaceManager.OnRaceStart -= OnRaceStart;
                eventManager.RaceManager.OnRaceResumed -= OnRaceStart;
                eventManager.RaceManager.OnRacePreStart -= OnRacePreStart;
                eventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
                tabbedMultiNode.OnTabChange -= OnTabChange;
                sceneManagerNode.ChannelsGridNode.OnGridCountChanged -= OnGridCountChanged;
            }
        }

        private void OnGridCountChanged(int count)
        {
            Triggers[] triggers = ChannelGrids.ToArray();

            int index = count - 1;

            if (index >= 0 && index < triggers.Length)
            {
                Trigger(triggers[index]);
            }
        }

        private void RemoteControl_Activity(bool success)
        {
            Activity?.Invoke(success);
        }

        public void Trigger(Triggers type)
        {
            if (!Active)
                return;

            if (remoteControl == null)
                return;

            if (type == lastTrigger && lastTriggerTime + doubleTriggerTimeout > DateTime.Now)
                return;

            lastTrigger = type;
            lastTriggerTime = DateTime.Now;

            foreach (OBSRemoteControlEvent rcEvent in config.RemoteControlEvents)
            {
                if (rcEvent.Trigger == type) 
                {
                    Logger.OBS.LogCall(this, rcEvent.GetType().Name, rcEvent.ToString());
                    
                    if (rcEvent is OBSRemoteControlSetSceneEvent)
                    {
                        OBSRemoteControlSetSceneEvent a = rcEvent as OBSRemoteControlSetSceneEvent;
                        remoteControl.SetScene(a.SceneName);
                    }
                    else if (rcEvent is OBSRemoteControlSourceFilterToggleEvent)
                    {
                        OBSRemoteControlSourceFilterToggleEvent a = rcEvent as OBSRemoteControlSourceFilterToggleEvent;
                        remoteControl.SetSourceFilterEnabled(a.SourceName, a.FilterName, a.Enable);
                    }
                    else if (rcEvent is OBSRemoteControlHotKeyEvent)
                    {
                        OBSRemoteControlHotKeyEvent a = rcEvent as OBSRemoteControlHotKeyEvent;
                        remoteControl.TriggerHotKeySequence(a.HotKey, a.Modifiers);
                    }
                    else if (rcEvent is OBSRemoteControlActionEvent)
                    {
                        OBSRemoteControlActionEvent a = rcEvent as OBSRemoteControlActionEvent;
                        remoteControl.TriggerHotKeyAction(a.ActionName);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
            }
        }

        private void OnTabChange(string tab, Node node)
        {
            if (tabbedMultiNode.IsOnLive) Trigger(Triggers.LiveTab);
            if (tabbedMultiNode.IsOnRounds) Trigger(Triggers.RoundsTab);
            if (tabbedMultiNode.IsOnChanelList) Trigger(Triggers.ChannelListTab);
            if (tabbedMultiNode.IsOnLapRecords) Trigger(Triggers.LapRecordsTab);
            if (tabbedMultiNode.IsOnLapCount) Trigger(Triggers.LapCountTab);
            if (tabbedMultiNode.IsOnRSSI) Trigger(Triggers.RSSITab);
            if (tabbedMultiNode.IsOnChanelList) Trigger(Triggers.ChannelListTab);
            if (tabbedMultiNode.IsOnPhotoBooth) Trigger(Triggers.PhotoBoothTab);
            if (tabbedMultiNode.IsOnPatreons) Trigger(Triggers.PatreonsTab);
            if (tabbedMultiNode.IsOnPoints) Trigger(Triggers.PointsTab);
            if (tabbedMultiNode.IsOnReplay) Trigger(Triggers.ReplayTab);
        }

        private void OnRacePreStart(Race race)
        {
            Trigger(Triggers.ClickStartRace);
        }

        private void OnRaceStart(Race race)
        {
            sectorCounter = -1;
            Trigger(Triggers.StartRaceTone);
        }

        private void RaceManager_OnRaceEnd(Race race)
        {
            sectorCounter = -1;
            Trigger(Triggers.RaceEnd);
        }

        private void RaceManager_OnRaceCancelled(Race arg1, bool arg2)
        {
            Trigger(Triggers.RaceStartCancelled);
        }

        private void OnSceneChange(SceneManagerNode.Scenes scene)
        {
            switch (scene) 
            {
                default:
                    Trigger(Triggers.LiveScene);
                    break;

                case SceneManagerNode.Scenes.PreRace:
                    Trigger(Triggers.PreRaceScene);
                    break;

                case SceneManagerNode.Scenes.RaceResults:
                    Trigger(Triggers.PostRaceScene);
                    break;
            }
        }


        [XmlInclude(typeof(OBSRemoteControlEvent)),
         XmlInclude(typeof(OBSRemoteControlSetSceneEvent)),
         XmlInclude(typeof(OBSRemoteControlSourceFilterToggleEvent)),
         XmlInclude(typeof(OBSRemoteControlHotKeyEvent))
            ]

        public class OBSRemoteControlConfig
        {
            public bool Enabled { get; set; }

            [Category("Connection")]
            public string Host { get; set; }
            [Category("Connection")]

            public int Port { get; set; }
            [Category("Connection")]

            public string Password { get; set; }

            [Browsable(false)]
            public List<OBSRemoteControlEvent> RemoteControlEvents { get; set; }

            public OBSRemoteControlConfig()
            {
                Enabled = false;
                Host = "localhost";
                Port = 4455;
#if DEBUG
                Password = "42ZzDvzK3Cd43HQW";
#endif
                RemoteControlEvents = new List<OBSRemoteControlEvent>();
            }

            protected const string filename = "OBSRemoteControl.xml";
            public static OBSRemoteControlConfig Load(Profile profile)
            {
                OBSRemoteControlConfig config = new OBSRemoteControlConfig();

                bool error = false;
                try
                {
                    OBSRemoteControlConfig[] s = IOTools.Read<OBSRemoteControlConfig>(profile, filename);

                    if (s != null && s.Any())
                    {
                        config = s[0];
                        Write(profile, config);
                    }
                    else
                    {
                        error = true;
                    }
                }
                catch
                {
                    error = true;
                }

                if (error)
                {
                    OBSRemoteControlConfig s = new OBSRemoteControlConfig();
                    Write(profile, s);
                    config = s;
                }

                return config;
            }

            public static void Write(Profile profile, OBSRemoteControlConfig s)
            {
                IOTools.Write(profile, filename, s);
            }

            public override string ToString()
            {
                return "OBS Remote Control Config";
            }
        }

        public abstract class OBSRemoteControlEvent
        {
            public Triggers Trigger { get; set; }
        }

        public class OBSRemoteControlSetSceneEvent : OBSRemoteControlEvent
        {
            public string SceneName { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + SceneName;
            }
        }

        public class OBSRemoteControlSourceFilterToggleEvent : OBSRemoteControlEvent
        {
            public string SourceName { get; set; }
            public string FilterName { get; set; }
            public bool Enable { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + SourceName + " " + FilterName + " " + Enable;
            }
        }

        public class OBSRemoteControlActionEvent : OBSRemoteControlEvent
        {
            public string ActionName { get; set; }

            public override string ToString()
            {
                return Trigger + " -> " + ActionName;
            }
        }

        public class OBSRemoteControlHotKeyEvent : OBSRemoteControlEvent
        {
            public OBSHotkey HotKey { get; set; }
            public KeyModifier Modifier1 { get; set; }
            public KeyModifier Modifier2 { get; set; }
            public KeyModifier Modifier3 { get; set; }

            [Browsable(false)]
            public KeyModifier Modifiers
            {
                get
                {
                    return Modifier1 | Modifier2 | Modifier3;
                }
            }

            public override string ToString()
            {
                string mods = "";
                if (Modifier1 != KeyModifier.None) mods += Modifier1 + " + ";
                if (Modifier2 != KeyModifier.None) mods += Modifier2 + " + ";
                if (Modifier3 != KeyModifier.None) mods += Modifier3 + " + ";

                return Trigger + " -> " + mods + HotKey;
            }
        }
    }
}
