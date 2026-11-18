using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2;

internal sealed partial class CombatSimulator2Mod : IMod, IWorkerMod
{
    /// <summary>Serves the live game's runs in the simulator host until the game stops or goes.</summary>
    public void RunWorker(WorkerContext context) => HostServer.Run(context);

    public void Load(ModContext context)
    {
        Log.Use(context.Log);
        MainThread.Init();
        LiveGame.SteamBuild = GameBuilds.ReadSteamBuild(context.GameDirectory);
        LiveGame.Checked = LiveGame.CheckedFor(context.Manifest, null);
        // The version text reads the game's project settings, which aren't there before the tree is ready.
        context.OnReady(_ => LiveGame.Checked = LiveGame.CheckedFor(context.Manifest, LiveGame.GameVersion));
        Dependencies.UseLevers(context.DataRoot);
        var deps = Dependencies.Resolve(null);
        Log.Info($"dependencies {deps.Resolved}/{deps.Total} resolved");
        foreach (var missing in deps.Missing)
            Log.Warn($"missing {missing} → {missing.Feature}");
        // A guard or a copy's own patch that can't attach: CS2 doesn't run without it. Failing here undoes the load.
        if (
            deps.Missing.FirstOrDefault(d => d.Kind == "patch" && Dependencies.Required.Contains(d.Feature)) is { } part
        )
            throw new InvalidOperationException($"{part} is missing on this game build; CS2 can't run without it");
        context.PatchAll();
        context.OnExit(ReleasePanelResources);
        // The worker registry: the game's exe, and every worker stopped when the game exits.
        Workers.Start();
        HostClient.Launcher = context.StartWorker;
        HostClient.Environment = HostEnvironment;
        HostClient.DataRoot = context.DataRoot;
        Reports.DataRoot = context.DataRoot;
        Reports.CrashFilesBytes = PanelState.Prefs.CrashFilesMegabytes * 1024L * 1024;
        context.OnExit(() => KeepGameLog(context.DataRoot));
        context.OnReady(_ => HostClient.LiveIdentity = ThisProcess.Identity());
        RunService.DefaultWhere = () => PanelState.Prefs.SimulateIn;
        RunService.DefaultTieShare = () => PanelState.Prefs.TieShare;
        SpoilerGuard.IsOn = () => PanelState.Prefs.SpoilerGuard;
        RunService.Remote = RemotePool.Starter;
        RunService.HostStatus = () => HostClient.Instance;
        RunService.Instance.FailureLog.Recorded += record =>
        {
            if (record.HasBanner)
                PanelState.SetBanner(BannerState.Failure.Of(record, RunService.Instance.Current));
        };
        BeforeFrameCallbacks(context);
        context.OnFrame(MainThread.Drain);
        context.OnFrame(LiveGame.Watch);
        context.OnFrame(RunService.Instance.OnFrame);
        context.OnFrame(PerfRecorder.OnFrame);
        StartStore(context);
        LiveGame.Left += LeftGame;
        PanelState.Load(context);
        PanelState.ApplyLanguage(context);
        RunArgs.Selection = PanelState.ToRun;
        Cs2RunCommands.Add(Cs2Command.Subs);
        Cs2CompareCommands.Add(Cs2Command.Subs);
        Cs2WhatIfCommands.Add(Cs2Command.Subs);
        Cs2SetupCommands.Add(Cs2Command.Subs);
        Cs2SideCommands.Add(Cs2Command.Subs);
        Cs2GearCommands.Add(Cs2Command.Subs);
        Cs2TargetCommands.Add(Cs2Command.Subs);
        Cs2BlocksCommands.Add(Cs2Command.Subs);
        Cs2LoadoutsCommands.Add(Cs2Command.Subs);
        Cs2PresetCommands.Add(Cs2Command.Subs);
        Cs2PageCommands.Add(Cs2Command.Subs);
        Cs2LineCommands.Add(Cs2Command.Subs);
        Cs2ReportCommands.Add(Cs2Command.Subs);
        Cs2UiCommands.Add(Cs2Command.Subs, () => HeaderState + "\nheader_icon=" + HeaderIcon);
        RunService.PanelState = () => Cs2Modal.IsOpen ? ReportPanel.Masked(Cs2UiCommands.Text()) : null;
        Cs2Modal.Register(context);
        Panel.Help.AboutIcons.Folder = context.Directory;
        Usage.Start(context);
        Panel.Report.ReportNote.Register(context);
        Tr.Use(context.Text);
        context.Text.Changed += LanguageChanged;
        context.Menu.AddAction(Tr.Key("Combat Simulator 2"), () => Cs2Modal.Open());
        _header = context.Header.Add("Cs2Sim", Tr.Key("Sim"), null, Tr.Key("Combat Simulator 2"), Cs2Modal.Toggle);
        _header.Visible = PanelState.Prefs.ShowHeaderButton;
        Cs2Modal.HeaderButtonChanged += on => _header.Visible = on;
        Cs2Modal.OpenChanged += ShowHeaderIcon;
        RunStore.Shared.Max = PanelState.Prefs.KeepRuns;
        context.OnFrame(Cs2Modal.OnFrame);
        context.OnReady(_ => Art.Preload([.. Art.Paths, .. Art.Boxes.Select(b => Art.BoxFolder + b + ".tres")]));
        // Godot's image loading isn't safe before the tree is ready.
        context.OnReady(_ =>
        {
            _closedIcon = HeaderArt(context.Directory, "header.png");
            _openIcon = HeaderArt(context.Directory, "header-selected.png");
            Panel.Help.AboutIcons.Mark = _openIcon;
            ShowHeaderIcon(Cs2Modal.IsOpen);
        });
        Cs2Command.Register(context.Commands);
#if DEBUG
        Commands.Dev.Cs2DevCommand.Register(context);
#endif
    }

    // Dev builds only: a frame callback of its own ahead of CS2's, to measure what they cost between them.
    static partial void BeforeFrameCallbacks(ModContext context);

    // Dev builds only: a measuring run is shown like any other but neither saved nor reused.
    static partial void Keeping(ref bool keep);

    private static Action? _indexSavedRuns;
    private static HeaderButton? _header;
    private static Godot.Texture2D? _closedIcon;
    private static Godot.Texture2D? _openIcon;

    // The Sim button's state for replies: hidden when switched off, absent without a loaded character, else shown.
    private static string HeaderState =>
        _header is not { Visible: true } ? "hidden"
        : LiveGame.Find() == null ? "absent"
        : "shown";

    // Which of CS2's two icons the Sim button shows, for replies: none when the button has neither.
    private static string HeaderIcon =>
        _header?.Icon is not { } icon ? "none"
        : ReferenceEquals(icon, _openIcon) ? "selected"
        : "normal";

    // The Sim button says whether CS2's window is open: its icon with corner brackets while it is.
    private static void ShowHeaderIcon(bool open)
    {
        if (_header != null && (open ? _openIcon : _closedIcon) is { } icon)
            _header.Icon = icon;
    }

    // One of the mod's own icons, with smaller copies so it stays sharp at the header's size.
    private static Godot.ImageTexture? HeaderArt(string directory, string file)
    {
        var path = Path.Combine(directory, "data", "icons", file);
        if (!File.Exists(path))
        {
            Log.Warn($"header icon {file} not found: the Sim button shows the game's Guide icon");
            return null;
        }
        var image = Godot.Image.LoadFromFile(path);
        image.GenerateMipmaps();
        return Godot.ImageTexture.CreateFromImage(image);
    }

    // Runs live beside the settings file, under the data root, so they survive reinstalls like settings do.
    private static void StartStore(ModContext context)
    {
        var folder = Path.Combine(context.DataRoot, "runs");
        var store = RunStore.Shared = new RunStore(folder);
        var index = RunService.Results = new ResultIndex(id =>
        {
            try
            {
                return store.Read(id);
            }
            catch (RunFileException e)
            {
                Log.Warn($"saved run {id} unreadable, its results aren't reused", e);
                return null;
            }
        })
        {
            CountsFights = () => Dependencies.IsAvailable(Dependencies.FightCounts),
        };
        var answers = Engine.Search.KeptAnswers.Store = new Model.Search.AnswerStore(context.DataRoot);
        if (answers.Load() is { } problem)
            Log.Warn($"kept search answers not read: {problem}");
        var service = RunService.Instance;
        service.KnownIds = () => store.List().Select(h => h.Id);
        service.Keeper = new ComparisonKeeper(store, service.FailureLog);
        // A comparison's runs stay while it is kept or on screen, however many runs come after.
        store.Pinned = service.ComparisonRuns;
        service.Finalised += run =>
        {
            var keep = true;
            Keeping(ref keep);
            if (!keep)
                return;
            index.Add(run); // at once, so a repeat started before the file is written finds it
            Task.Run(() =>
            {
                try
                {
                    store.Write(run);
                    Log.Info($"{run.Id} saved");
                    index.Saved(run.Id);
                    index.Keep(store.List().Select(h => h.Id));
                }
                catch (RunFileException e)
                {
                    Log.Error($"{run.Id} not saved", e);
                    RunFailures.SaveFailed(service.FailureLog, run.Id, e);
                }
            });
        };
        // The saved runs' results, read once a character is in game and never before: reading a full store takes
        // seconds of unzipping and parsing, which made the game's own start and character load slower when it ran
        // beside them. One thread below normal priority, so it yields to the game after the load too.
        _indexSavedRuns = () =>
        {
            LiveGame.Entered -= _indexSavedRuns;
            new Thread(() =>
            {
                foreach (var header in store.List())
                {
                    try
                    {
                        index.Add(store.Read(header.Id), saved: true);
                    }
                    catch (RunFileException e)
                    {
                        Log.Warn("saved run not indexed: " + e.Message);
                    }
                }
                Log.Info($"result index: {index.Count} results");
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "cs2-result-index",
            }.Start();
        };
        LiveGame.Entered += _indexSavedRuns;
        RunFinder.Saved = id =>
        {
            try
            {
                return store.Read(id);
            }
            catch (RunFileException e)
            {
                Log.Warn($"saved run {id} unreadable", e);
                return null;
            }
        };
        if (store.Newest() is { } newest)
            service.Show(newest);
        if (service.Keeper.Newest() is { } comparison)
            service.Show(comparison);
        foreach (var skipped in store.Skipped)
            Log.Warn("saved run skipped: " + skipped);
    }

    // A new language: cached text is stale, and the window is made again in it.
    private static void LanguageChanged()
    {
        Tr.Changed();
        RunCache.Clear();
        RunService.Instance.ClearSheets();
        Cs2Modal.Retext();
    }

    // Back to the title screen mid-run: cancel it (finished results are kept and saved).
    private static void LeftGame()
    {
        if (RunService.Instance.IsActive)
            RunService.Instance.Cancel(Note.Of("cancelled: the character was closed"));
    }

    // Workers still running lose their guards once Core removes the patches: stop them first.
    public void Unload(ModContext context)
    {
        LiveGame.Left -= LeftGame;
        LiveGame.Entered -= _indexSavedRuns;
        _indexSavedRuns = null;
        context.Text.Changed -= LanguageChanged;
        Tr.Use(null);
#if DEBUG
        Commands.Dev.Cs2DevCommand.Unloading();
#endif
        if (RunService.Instance.IsActive)
            RunService.Instance.Cancel(Note.Of("cancelled: the mod was reloaded"));
        RunService.Reset();
        // The dev host keeps a reloaded mod's old copy loaded, and with it whatever its statics and callbacks still
        // reach: the saved results would stay, hundreds of megabytes a reload. Emptied, not only let go: the store's
        // callbacks hold the index too.
        RunService.Results?.Clear();
        RunService.Results = null;
        ChangeCheck.Release();
        HostClient.Reset();
        PerfRecorder.Clear();
        HostClient.Launcher = null;
        HostClient.LiveIdentity = null;
        Usage.Reset();
        Panel.Help.AboutIcons.Clear();
        PanelState.Reset();
        Cs2Modal.Reset();
        Panel.Report.ReportNote.Reset();
        Look.Clear();
        PixelDraw.Clear();
        Art.Clear();
        Badge.Clear();
        Panel.Compare.CompareText.Clear();
        RunCache.Clear();
        Motion.Clear();
        _header = null;
        _closedIcon = null;
        _openIcon = null;
        MainThread.Clear();
        Cs2Command.Subs.Clear();
#if DEBUG
        Commands.Dev.Cs2DevCommand.Subs.Clear();
#endif
        // Last: the host client above has stopped talking to the host.
        context.StopWorkers();
    }

    // The game is quitting: the panel's fonts, and the label settings that hold them, go back to Godot while its text
    // server still runs.
    private static void ReleasePanelResources()
    {
        Look.Release();
        Badge.Release();
        Cs2Check.Release();
        Panel.Help.AboutIcons.Clear();
        Panel.Compare.CompareText.Release();
    }

    // The shared log empties this file when the game next starts: a copy keeps this session's lines for a report.
    private static void KeepGameLog(string dataRoot)
    {
        var folder = Path.Combine(dataRoot, "logs");
        try
        {
            File.Copy(
                Path.Combine(folder, "CombatSimulator2.log"),
                Path.Combine(folder, "CombatSimulator2.previous.log"),
                overwrite: true
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("the game's log not kept", e);
        }
    }

    // The host's runtime settings, for its process only; the live game's runtime is untouched. The runtime
    // reads the numbers as hex.
    private static Dictionary<string, string> HostEnvironment() =>
        new()
        {
            ["DOTNET_gcServer"] = "1",
            // One heap per copy plus the two built ahead. Letting the collector pick the count as it goes made
            // runs up to half as long again.
            ["DOTNET_GCHeapCount"] = (Engine.DefaultCopies.Now + 2).ToString(
                "x",
                System.Globalization.CultureInfo.InvariantCulture
            ),
            // A heap collects after 32 MB of new objects rather than 200: a run's peak is a few hundred MB lower
            // for a few percent of its time.
            ["DOTNET_GCgen0MaxBudget"] = "2000000",
        };
}
