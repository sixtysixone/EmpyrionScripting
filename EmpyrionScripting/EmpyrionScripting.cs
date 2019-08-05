﻿using Eleon.Modding;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using EmpyrionNetAPITools.Extensions;
using EmpyrionScripting.CustomHelpers;
using EmpyrionScripting.DataWrapper;
using HandlebarsDotNet;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EmpyrionScripting
{

    public class EmpyrionScripting : ModInterface, IMod, IDisposable
    {
        public static event EventHandler StopScriptsEvent;

        private const string TargetsKeyword  = "Targets:";
        private const string ScriptKeyword   = "Script:";
        ModGameAPI legacyApi;

        ConcurrentDictionary<string, Func<object, string>> LcdCompileCache = new ConcurrentDictionary<string, Func<object, string>>();

        public ScriptExecQueue ScriptExecQueue { get; set; }

        public static EmpyrionScripting EmpyrionScriptingInstance { get; set; }
        public static ItemInfos ItemInfos { get; set; }
        public string SaveGameModPath { get; set; }
        public static ConfigurationManager<Configuration> Configuration { get; private set; }
        public static Localization Localization { get; set; }
        public static IModApi ModApi { get; private set; }
        public SaveGamesScripts SaveGamesScripts { get; private set; }
        public string L { get; private set; }
        public bool DeviceLockAllowed => (CycleCounter % Configuration.Current.DeviceLockOnlyAllowedEveryXCycles) == 0;

        public bool PauseScripts { get; private set; } = true;
        public IEntity[] CurrentEntities { get; private set; }
        public DateTime LastAlive { get; private set; }

        private static int CycleCounter;

        public EmpyrionScripting()
        {
            EmpyrionScriptingInstance     = this;
            EmpyrionConfiguration.ModName = "EmpyrionScripting";
            ScriptExecQueue.Log           = Log;
            ScriptExecQueue               = new ScriptExecQueue(ProcessScript);
            SetupHandlebarsComponent();
        }

        public void Init(IModApi modAPI)
        {
            ModApi = modAPI;

            ModApi.Log("EmpyrionScripting Mod started: IModApi");
            try
            {
                SetupHandlebarsComponent();

                Localization    = new Localization(ModApi.Application?.GetPathFor(AppFolder.Content));
                ItemInfos       = new ItemInfos(ModApi.Application?.GetPathFor(AppFolder.Content), Localization);
                SaveGameModPath = Path.Combine(ModApi.Application?.GetPathFor(AppFolder.SaveGame), "Mods", EmpyrionConfiguration.ModName);

                LoadConfiguration();
                SaveGamesScripts = new SaveGamesScripts(modAPI) { SaveGameModPath = SaveGameModPath };
                SaveGamesScripts.ReadSaveGamesScripts();

                TaskTools.Log = ModApi.LogError;

                ModApi.Application.OnPlayfieldLoaded   += Application_OnPlayfieldLoaded;
                ModApi.Application.OnPlayfieldUnloaded += Application_OnPlayfieldUnloaded;

                StopScriptsEvent += (S, E) =>
                {
                    ModApi.Log($"StopScriptsEvent: {(PauseScripts ? "always stopped" : "scripts running")}");
                    PauseScripts = true;
                };

                StartAllScriptsForPlayfieldServer();
            }
            catch (Exception error)
            {
                ModApi.LogError($"EmpyrionScripting Mod init finish: {error}");
            }

            ModApi.Log("EmpyrionScripting Mod init finish");

        }

        private void LoadConfiguration()
        {
            ConfigurationManager<Configuration>.Log = ModApi.Log;
            Configuration = new ConfigurationManager<Configuration>()
            {
                ConfigFilename = Path.Combine(SaveGameModPath, "Configuration.json")
            };
            Configuration.Load();
            Configuration.Save();
        }

        public void Shutdown()
        {
            ModApi.Log("Mod exited:Shutdown");
            StopScriptsEvent.Invoke(this, EventArgs.Empty);
        }

        private void SetupHandlebarsComponent()
        {
            Handlebars.Configuration.TextEncoder = null;
            HelpersTools.ScanHandlebarHelpers();
        }

        private void Application_OnPlayfieldLoaded(string playfieldName)
        {
            ModApi.Log($"StartScripts for {playfieldName} pending");
            TaskTools.Delay(Configuration.Current.DelayStartForNSecondsOnPlayfieldLoad, () => {
                ModApi.Log($"StartScripts for {playfieldName}");
                PauseScripts = false;
            });
        }

        private void Application_OnPlayfieldUnloaded(string playfieldName)
        {
            ModApi.Log($"PauseScripts for {playfieldName} {(PauseScripts ? "always stopped" : "scripts running")}");
            PauseScripts = true;
        }

        public void StartAllScriptsForPlayfieldServer()
        {
            ModApi.Log($"StartAllScriptsForPlayfieldServer: InGame:{Configuration.Current.InGameScriptsIntervallMS}ms SaveGame:{Configuration.Current.SaveGameScriptsIntervallMS}ms ");

            StartScriptIntervall(Configuration.Current.InGameScriptsIntervallMS, () =>
            {
                Log($"InGameScript: {PauseScripts} #{CycleCounter}", LogLevel.Debug);
                LastAlive = DateTime.Now;
                if (PauseScripts) return;

                Interlocked.Increment(ref CycleCounter);
                UpdateScripts(ProcessAllInGameScripts, "InGameScript");
            }, "InGameScript");

            StartScriptIntervall(Configuration.Current.SaveGameScriptsIntervallMS, () =>
            {
                Log($"SaveGameScript: {PauseScripts}", LogLevel.Debug);
                LastAlive = DateTime.Now;
                if (PauseScripts) return;

                UpdateScripts(ProcessAllSaveGameScripts, "SaveGameScript");
            }, "SaveGameScript");

            StartScriptIntervall(60000, () =>
            {
                Log($"ScriptInfos: {ScriptExecQueue.ScriptRunInfo.Count}", LogLevel.Debug);
                LastAlive = DateTime.Now;
                if (PauseScripts || Configuration.Current.LogLevel > LogLevel.Message) return;

                if(Configuration.Current.LogLevel != LogLevel.Debug) Log($"ScriptInfos: {ScriptExecQueue.ScriptRunInfo.Count}", LogLevel.Message);
                ScriptExecQueue.ScriptRunInfo
                    .OrderBy(I => I.Key)
                    .ForEach(I => Log($"Script: {I.Key,-50} #{I.Value.Count,5} LastStart:{I.Value.LastStart} ExecTime:{I.Value.ExecTime}", LogLevel.Message));
            }, "ScriptInfos");

        }

        private void StartScriptIntervall(int intervall, Action action, string name)
        {
            if (intervall <= 0) return;

            var exec = TaskTools.Intervall(intervall, action, name);
            StopScriptsEvent += (S, E) => exec.Set();
        }

        // Called once early when the game starts (but not again if player quits from game to title menu and starts (or resumes) a game again
        // Hint: treat this like a constructor for your mod
        public void Game_Start(ModGameAPI legacyAPI)
        {
            legacyApi = legacyAPI;
            legacyApi?.Console_Write("EmpyrionScripting Mod started: Game_Start");
        }

        public static string ErrorFilter(Exception error) => Configuration.Current.LogLevel == EmpyrionNetAPIDefinitions.LogLevel.Debug ? error.ToString() : error.Message;

        private void UpdateScripts(Action<IEntity> process, string name)
        {
            try
            {
                if (ModApi.Playfield          == null) { ModApi.Log($"UpdateScripts no Playfield"); return; }
                if (ModApi.Playfield.Entities == null) { ModApi.Log($"UpdateScripts no Entities");  return; }

                var timer = new Stopwatch();
                timer.Start();

                CurrentEntities = ModApi.Playfield.Entities
                    .Values
                    .Where(E => E.Type == EntityType.BA ||
                                E.Type == EntityType.CV ||
                                E.Type == EntityType.SV ||
                                E.Type == EntityType.HV)
                    .ToArray();

                Log($"CurrentEntities: {CurrentEntities.Length}", LogLevel.Debug);

                CurrentEntities.ForEach(process);

                timer.Stop();
                if(timer.Elapsed.TotalSeconds > 30) Log($"UpdateScripts: {name} RUNS {timer.Elapsed} !!!!", LogLevel.Message);
                else                                Log($"UpdateScripts: {name} take {timer.Elapsed}",      LogLevel.Debug);

            }
            catch (Exception error)
            {
                ModApi.LogWarning("Next try because: " + ErrorFilter(error));
            }
        }

        private void Log(string text, LogLevel level)
        {
            if(Configuration.Current.LogLevel <= level) ModApi.Log(text);
        }

        private void ProcessAllInGameScripts(IEntity entity)
        {
            Log($"ProcessAllInGameScripts: {entity.Name}:{entity.Type} Pause:{PauseScripts}", LogLevel.Debug);
            if (entity.Type == EntityType.Proxy || PauseScripts) return;

            try
            {
                var entityScriptData = new ScriptRootData(CurrentEntities, ModApi.Playfield, entity, DeviceLockAllowed);

                var deviceNames = entityScriptData.E.S.AllCustomDeviceNames.Where(N => N.StartsWith(ScriptKeyword)).ToArray();
                Log($"ProcessAllInGameScripts: #{deviceNames.Length}", LogLevel.Debug);

                Parallel.ForEach(deviceNames, N =>
                {
                    if (PauseScripts) return;

                    var lcd = entity.Structure.GetDevice<ILcd>(N);
                    if (lcd == null) return;

                    try
                    {
                        Log($"ProcessAllInGameScripts: {N}", LogLevel.Debug);

                        var data = new ScriptRootData(entityScriptData)
                        {
                            Script = lcd.GetText(),
                            Error  = L,
                        };

                        AddTargetsAndDisplayType(data, N.Substring(ScriptKeyword.Length));

                        if (Configuration.Current.ScriptTracking)
                        {
                            var trackfile = GetTrackingFileName(entity, data.Script.GetHashCode().ToString());
                            if(!File.Exists(trackfile)) File.WriteAllText(trackfile, data.Script);
                        }

                        data.ScriptId = entity.Id + "/" + N;
                        ScriptExecQueue.Add(data);
                    }
                    catch (Exception lcdError)
                    {
                        if(Configuration.Current.LogLevel >= EmpyrionNetAPIDefinitions.LogLevel.Debug)
                            ModApi.Log($"UpdateLCDs ({entity.Id}/{entity.Name}):LCD: {lcdError}");
                    }
                });
            }
            catch (Exception error)
            {
                File.WriteAllText(GetTrackingFileName(entity, string.Empty) + ".error", error.ToString());
            }
        }

        private string GetTrackingFileName(IEntity entity, string scriptid)
        {
            var trackfile = Path.Combine(SaveGameModPath, "ScriptTracking", entity == null ? "" : entity.Id.ToString(), $"{entity?.Id}-{entity?.Type}-{scriptid}.hbs");
            Directory.CreateDirectory(Path.GetDirectoryName(trackfile));
            return trackfile;
        }

        private void ProcessAllSaveGameScripts(IEntity entity)
        {
            if (entity.Type == EntityType.Proxy || PauseScripts) return;

            try
            {
                var entityScriptData = new ScriptSaveGameRootData(CurrentEntities, ModApi.Playfield, entity)
                {
                    MainScriptPath = SaveGamesScripts.MainScriptPath,
                    ModApi         = ModApi
                };

                ExecFoundSaveGameScripts(entityScriptData, 
                    Path.Combine(SaveGamesScripts.MainScriptPath, Enum.GetName(typeof(EntityType), entity.Type)),
                    Path.Combine(SaveGamesScripts.MainScriptPath, entity.Name),
                    Path.Combine(SaveGamesScripts.MainScriptPath, ModApi.Playfield.Name),
                    Path.Combine(SaveGamesScripts.MainScriptPath, ModApi.Playfield.Name, Enum.GetName(typeof(EntityType), entity.Type)),
                    Path.Combine(SaveGamesScripts.MainScriptPath, ModApi.Playfield.Name, entity.Name),
                    Path.Combine(SaveGamesScripts.MainScriptPath, entity.Id.ToString())
                    );
            }
            catch (Exception error)
            {
                File.WriteAllText(GetTrackingFileName(entity, "SaveGameScript") + ".error", error.ToString());
            }
        }

        public void ExecFoundSaveGameScripts(ScriptSaveGameRootData entityScriptData, params string[] scriptLocations)
        {
            scriptLocations
                .ForEach(S =>
                {
                    if (PauseScripts) return;

                    var path = S.NormalizePath();

                    if (SaveGamesScripts.SaveGameScripts.TryGetValue(path + SaveGamesScripts.ScriptExtension, out var C)) ProcessScript(new ScriptSaveGameRootData(entityScriptData) {
                        Script     = C,
                        ScriptPath = Path.GetDirectoryName(path)
                    });
                    else SaveGamesScripts.SaveGameScripts
                        .Where(F => Path.GetDirectoryName(F.Key) == path)
                        .ForEach(F => ProcessScript(new ScriptSaveGameRootData(entityScriptData) {
                            Script     = F.Value,
                            ScriptPath = Path.GetDirectoryName(F.Key)
                        }));
                });
        }

        private static void AddTargetsAndDisplayType(ScriptRootData data, string targets)
        {
            if (targets.StartsWith("["))
            {
                var typeEnd = targets.IndexOf(']');
                if(typeEnd > 0)
                {
                    var s = targets.Substring(1, typeEnd - 1);
                    var appendAtEnd = s.EndsWith("+");
                    int.TryParse(appendAtEnd ? s.Substring(0, s.Length - 1) : s.Substring(1), out int Lines);
                    data.DisplayType = new DisplayOutputConfiguration() { AppendAtEnd = appendAtEnd, Lines = Lines };

                    targets = targets.Substring(typeEnd + 1);
                }
            }

            data.LcdTargets.AddRange(data.E.S.AllCustomDeviceNames.GetUniqueNames(targets).Where(N => !N.StartsWith(ScriptKeyword)));
        }

        private void ProcessScript<T>(T data) where T : ScriptRootData
        {
            try
            {
                if (PauseScripts) return;

                var result = ExecuteHandlebarScript(data, data.Script).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (result.Length > 0 && result[0].StartsWith(TargetsKeyword))
                {
                    AddTargetsAndDisplayType(data, result[0].Substring(TargetsKeyword.Length));
                    result = result.Skip(1).ToArray();
                }

                if (data.DisplayType != null) result = result
                                                .SkipWhile(string.IsNullOrWhiteSpace)
                                                .Reverse()
                                                .SkipWhile(string.IsNullOrWhiteSpace)
                                                .Reverse()
                                                .ToArray();

                data.LcdTargets
                    .Select(L => data.E.S.GetCurrent().GetDevice<ILcd>(L))
                    .Where(L => L != null)
                    .ForEach(L =>
                    {
                        if (PauseScripts) return;

                        if (data.DisplayType == null) L.SetText(string.Join("\n", result));
                        else
                        {
                            var text = L.GetText().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                            L.SetText(string.Join("\n", data.DisplayType.AppendAtEnd 
                                    ? text  .Concat(result).TakeLast(data.DisplayType.Lines)
                                    : result.Concat(text  ).Take    (data.DisplayType.Lines)
                                ));
                        }

                        if (data.ColorChanged          ) L.SetColor     (data.Color);
                        if (data.BackgroundColorChanged) L.SetBackground(data.BackgroundColor);
                        if (data.FontSizeChanged       ) L.SetFontSize  (data.FontSize);
                    });
            }
            catch (Exception ctrlError)
            {
                File.WriteAllText(GetTrackingFileName(data.E.GetCurrent(), data.Script.GetHashCode().ToString()) + ".error", ctrlError.ToString());

                if (PauseScripts) return;
                data.LcdTargets.ForEach(L => data.E.S.GetCurrent().GetDevice<ILcd>(L)?.SetText($"{ctrlError.Message} {DateTime.Now.ToLongTimeString()}"));
            }
        }

        public string ExecuteHandlebarScript<T>(T data, string script)
        {
            if(!LcdCompileCache.TryGetValue(script, out Func<object, string> generator))
            {
                generator = Handlebars.Compile(script);
                LcdCompileCache.TryAdd(script, generator);
            }

            return generator(data);
        }

        public void Game_Exit()
        {
            ModApi.Log("Mod exited:Game_Exit");
            StopScriptsEvent?.Invoke(this, EventArgs.Empty);
        }

        public void Game_Update()
        {
            //Log("EmpyrionScripting Mod: Game_Update", LogLevel.Debug);
            if (!PauseScripts && (DateTime.Now - LastAlive).TotalSeconds > 120) RestartAllScriptsForPlayfieldServer();

            ScriptExecQueue.ExecNext();
        }

        public static void RestartAllScriptsForPlayfieldServer()
        {
            StopScriptsEvent?.Invoke(EmpyrionScriptingInstance, EventArgs.Empty);
            EmpyrionScriptingInstance.PauseScripts = false;
            Configuration.Load();
            ModApi?.Log($"EmpyrionScripting Mod.Restart Threads: {EmpyrionScriptingInstance.LastAlive} <-> {DateTime.Now} : {Configuration.Current.LogLevel}");
            EmpyrionScriptingInstance.StartAllScriptsForPlayfieldServer();
        }

        // called for legacy game events (e.g. Event_Player_ChangedPlayfield) and answers to requests (e.g. Event_Playfield_Stats)
        public void Game_Event(CmdId eventId, ushort seqNr, object data)
        {
            Log($"EmpyrionScripting Mod: Game_Event {eventId} {seqNr} {data}", LogLevel.Debug);
        }

        public void Dispose()
        {
            ModApi?.Log("EmpyrionScripting Mod: Dispose");
        }
    }

}
