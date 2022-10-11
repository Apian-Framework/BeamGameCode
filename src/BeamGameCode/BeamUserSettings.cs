using System.Runtime.Serialization;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace BeamGameCode
{
    public class UserSettingsException : Exception
    {
        public UserSettingsException(string message) : base(message) { }
    }

    public static class UserSettingsMgr
    {
        public const string currentVersion = "105";
        public const string subFolder = ".beam";
        public const string defaultBaseName= "beamsettings";
        public static string fileBaseName;
        public static string path =  GetPath(subFolder);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SyncFiles();

        [DllImport("__Internal")]
        public static extern string Get_WebGLDefaultSettings();

#endif

        public static BeamUserSettings Load(string baseName = defaultBaseName)
        {
            fileBaseName = baseName;
            BeamUserSettings settings;
            string filePath = path + Path.DirectorySeparatorChar + fileBaseName + ".json";
            try {
                settings = JsonConvert.DeserializeObject<BeamUserSettings>(File.ReadAllText(filePath));
                UniLogger.GetLogger("UserSettings").Info($"Loaded settings from: {filePath}.");
            } catch(Exception) {
                UniLogger.GetLogger("UserSettings").Info($"Old settings not found. Creating Defaults.");
                settings =  BeamUserSettings.CreateDefault();
            }

            // TODO: in real life this should do at least 1 version's worth of updating.
            // FIXME: Actually, this can't work at all using class-template-based serialization
            //  Need to decide if we REALLY want to support updating.
            if (settings.version != currentVersion)
                throw( new UserSettingsException($"Invalid settings version: {settings.version}. Expected: {currentVersion}"));

            return settings;
        }

        public static void Save(BeamUserSettings settings)
        {
            // TODO: UniLogger settings are handled inconsistently
            System.IO.Directory.CreateDirectory(path);
            string filePath = path + Path.DirectorySeparatorChar + fileBaseName + ".json";
            BeamUserSettings saveSettings = new BeamUserSettings(settings);
            saveSettings.tempSettings = new Dictionary<string, string>(); // Don't persist temp settings

            // Update w/ current UniLogger settings. Update any levels that have been change, but don't get rid of any
            // just because they aren't in CurrentLevels - (UniLog only creates a entry if the logger has been accessed)
            foreach (KeyValuePair<string, string > entry  in  UniLogger.CurrentLoggerLevels())
            {
                saveSettings.logLevels[entry.Key] = entry.Value;
            }
            // and grab/save the current default log level
            saveSettings.defaultLogLevel = UniLogger.LevelNames[UniLogger.DefaultLevel];

            UniLogger.GetLogger("UserSettings").Info($"Saving settings to {filePath}.");
            string JsonSettings =  JsonConvert.SerializeObject(saveSettings, Formatting.Indented);
            UniLogger.GetLogger("UserSettings").Debug($"Saved settings:\n{JsonSettings}.");
            File.WriteAllText(filePath,JsonSettings);
#if UNITY_WEBGL && !UNITY_EDITOR
            SyncFiles(); // browser FS is async - need to wait for write to complete
#endif
        }

        public static string GetPath(string leafFolder)
        {
#if UNITY_2019_1_OR_NEWER
            string homePath =  Application.persistentDataPath;

#else
            string homePath = (Environment.OSVersion.Platform == PlatformID.Unix ||
                        Environment.OSVersion.Platform == PlatformID.MacOSX)
                        ? Environment.GetEnvironmentVariable("HOME")
                        : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
#endif
            UniLogger.GetLogger("UserSettings").Info($"User settings path: {homePath + Path.DirectorySeparatorChar + leafFolder}");
            return homePath + Path.DirectorySeparatorChar + leafFolder;
        }

    }


    public class BeamUserSettings
    {
        public string version = UserSettingsMgr.currentVersion;
        public string startMode;
        public string screenName;
        public string p2pConnectionString;
        public string apianNetworkName;
        public string ethNodeUrl;
        public string cryptoAcctJSON; // serialized encrypted keystore
        public string localPlayerCtrlType;
        public int aiBikeCount; // in addition to localPLayerBike, spawn this many AIs (and respawn to keep the number up)
        public bool regenerateAiBikes; // create new ones when old ones get blown up

        public string defaultLogLevel;

        public Dictionary<string, string> logLevels;
        public Dictionary<string, string> tempSettings; // dict of cli-set, non-peristent values

        public Dictionary<string, string> platformSettings; // dict of persistent, but platform-specific, settings

        public BeamUserSettings()
        {
            logLevels = new Dictionary<string, string>();
            tempSettings = new Dictionary<string, string>();
            platformSettings = new Dictionary<string, string>();
        }

        public BeamUserSettings(BeamUserSettings source)
        {
            if (version != source.version)
                throw( new UserSettingsException($"Invalid source settings version: {source.version} Expected: {version}"));
            startMode = source.startMode;
            screenName = source.screenName;
            p2pConnectionString = source.p2pConnectionString;
            apianNetworkName = source.apianNetworkName;
            ethNodeUrl = source.ethNodeUrl;
            cryptoAcctJSON = source.cryptoAcctJSON;
            localPlayerCtrlType = source.localPlayerCtrlType;
            aiBikeCount = source.aiBikeCount;
            regenerateAiBikes = source.regenerateAiBikes;
            defaultLogLevel = source.defaultLogLevel;
            logLevels = source.logLevels ?? new Dictionary<string, string>();
            tempSettings = source.tempSettings ?? new Dictionary<string, string>();
            platformSettings = source.platformSettings ?? new Dictionary<string, string>();
        }

        public static BeamUserSettings CreateDefault()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL defaults are read from a static file.
            return JsonConvert.DeserializeObject<BeamUserSettings>(UserSettingsMgr.Get_WebGLDefaultSettings());
#else
            return new BeamUserSettings() {
                version = UserSettingsMgr.currentVersion,
                startMode = BeamModeFactory.NetworkModeName,
                screenName = "Fred Sanford",
                //p2pConnectionString = "p2predis::newsweasel.com,password=O98nfRVWYYHg7rXpygBCBZWl+znRATaRXTC469SafZU",
                p2pConnectionString = "p2pmqtt::{\"server\":\"newsweasel.com\",\"user\":\"apian_mqtt\",\"pwd\":\"apian_mqtt_pwd\"}",
                apianNetworkName = "BeamNet1",
                //p2pConnectionString = "p2predis::192.168.1.195,password=sparky-redis79",
                ethNodeUrl = "https://rinkeby.infura.io/v3/7653fb1ed226443c98ce85d402299735",
                cryptoAcctJSON = "",
                localPlayerCtrlType = BikeFactory.AiCtrl,
                aiBikeCount = 2,
                regenerateAiBikes = false,
                defaultLogLevel = "Warn",
                logLevels = new Dictionary<string, string>() {
                    {"UserSettings", UniLogger.LevelNames[UniLogger.Level.Info]},
                    {"P2pNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameNet", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"GameInstance", UniLogger.LevelNames[UniLogger.Level.Warn]},
                    {"BeamMode", UniLogger.LevelNames[UniLogger.Level.Warn]},
                },
                tempSettings = new Dictionary<string, string>(),
                platformSettings = new Dictionary<string, string>()
            };
#endif
        }

    }

}