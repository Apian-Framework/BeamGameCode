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
        public const string currentVersion = "107";
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
        public Dictionary<string, string> p2pConnectionSettings; // named connections. settings are connection-specific and are usually json
        public Dictionary<string, string> blockchainInfos; // chain info, keyed by an arbitrary name
        public Dictionary<string, string> gameAcctJSON; // eecrypted ephemeral acct keystores

        public string curP2pConnection; // a key from p2pConnectionSettings
        public string curBlockchain; // key into blockchainInfos
        public string gameAcctAddr; // address (key into gameAcctJSON)
        public string permAcctAddr; // address

        public string apianNetworkName;
        public string localPlayerCtrlType;
        public int aiBikeCount; // in addition to localPLayerBike, spawn this many AIs (and respawn to keep the number up)
        public bool regenerateAiBikes; // create new ones when old ones get blown up

        public string defaultLogLevel;

        public Dictionary<string, string> logLevels;
        public Dictionary<string, string> tempSettings; // dict of cli-set, non-peristent values

        public Dictionary<string, string> platformSettings; // dict of persistent, but platform-specific, settings

        public string GetTempSetting(string key)
        {
            return  tempSettings.ContainsKey(key) ? tempSettings[key] : null;
        }

        public BeamUserSettings()
        {
            p2pConnectionSettings =  new Dictionary<string,string>();
            blockchainInfos = new Dictionary<string,string>();
            gameAcctJSON = new Dictionary<string, string>();
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
            blockchainInfos = new Dictionary<string,string>(source.blockchainInfos);
            p2pConnectionSettings =  new Dictionary<string,string>(source.p2pConnectionSettings);
            gameAcctJSON = new Dictionary<string, string>(source.gameAcctJSON);
            curP2pConnection = source.curP2pConnection;
            apianNetworkName = source.apianNetworkName;
            curBlockchain = source.curBlockchain;
            permAcctAddr = source.permAcctAddr;
            gameAcctAddr = source.gameAcctAddr;
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
                blockchainInfos = new Dictionary<string,string>()
                {
                    {"ETH MainNet", "{\"RpcUrl\": \"https://mainnet.infura.io/v3/6f03e0922a574b58867988f047fd3cfc\", \"ChainId\": 1, \"Currency\": \"ETH\"}"},
                    {"ETH Gorli", "{\"RpcUrl\": \"https://goerli.infura.io/v3/6f03e0922a574b58867988f047fd3cfc\", \"ChainId\": 3, \"Currency\": \"ETH\"}"},
                    {"Gnosis Main", "{\"RpcUrl\": \"https://rpc.gnosischain.com\", \"ChainId\": 100, \"Currency\": \"xDAI\"}"},
                    {"Gnosis Chiado", "{\"RpcUrl\": \"https://rpc.chiadochain.net\", \"ChainId\": 10200, \"Currency\": \"xDAI\"}"}
                },
                p2pConnectionSettings = new Dictionary<string, string>()
                {
                    {"PokeyHedgehog MQTT", "p2pmqtt::{\"server\":\"pokeyhedgehog.com\",\"user\":\"apian_mqtt\",\"pwd\":\"apian_mqtt_pwd\"}"},
                    {"PokeyHedgehog Redis", "p2predis::pokeyhedgehog.com,password=Dga2JfGoKfDv02xWY0bYNrYaFPeBTVmXLPGDKq1xA"},
                    {"Sparky MQTT", "p2pmqtt::{\"server\":\"sparkyx\"}"}
                },
                gameAcctJSON = new Dictionary<string, string>(),
                curP2pConnection = "PokeyHedgehog MQTT",
                curBlockchain = "Gnosis Chiado",
                gameAcctAddr = "",
                permAcctAddr = "0x1234567890123456789012345678901234567890",
                apianNetworkName = "BeamNet1",
                localPlayerCtrlType = BikeFactory.AiCtrl,
                aiBikeCount = 0,
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