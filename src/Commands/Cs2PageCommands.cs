using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 pages</c> and <c>cs2 page</c>: a side's setup pages as the Setup tab lists and shows them.</summary>
internal static class Cs2PageCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new("pages", "cs2 pages [a|b]", [], [], Pages, Purpose: "One line per setup page of the side"),
            new(
                "page",
                "cs2 page <id> [a|b] [filter]",
                [],
                [],
                Page,
                Purpose: "A page's groups and entries; the filter matches entry ids and names"
            ),
        ]);

    private static void Pages(CommandCall call, CommandArgs args)
    {
        var side = Side(args.Word(0));
        Read(
            call,
            side,
            pages =>
            {
                var text = new StringBuilder();
                foreach (var page in pages)
                {
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"{page.Id} {page.Section.ToString().ToLowerInvariant()} {State(page)} changes={SetupPage.ChangeCount(page.Id, side.Changes)}"
                    );
                    if (page.Reason != null)
                        text.Append(' ').Append(page.Reason.English());
                    if (page.Gated)
                        text.Append(' ').Append(page.Gate.English("; "));
                    text.Append('\n');
                }
                return text.ToString().TrimEnd();
            }
        );
    }

    private static void Page(CommandCall call, CommandArgs args)
    {
        var id = args.Word(0)?.ToLowerInvariant() ?? throw new CommandException("usage: cs2 page <id> [a|b] [filter]");
        if (SetupPage.All.All(p => p.Id != id))
            throw new CommandException($"no page '{id}': {string.Join(", ", SetupPage.All.Select(p => p.Id))}");
        var hasSide = args.Word(1)?.ToLowerInvariant() is "a" or "b";
        var side = Side(hasSide ? args.Word(1) : null);
        var filter = args.Word(hasSide ? 2 : 1);
        Read(
            call,
            side,
            pages =>
            {
                var page = pages.First(p => p.Id == id);
                var text = new StringBuilder(
                    string.Create(CultureInfo.InvariantCulture, $"{page.Id} {State(page)} groups={page.Groups.Count}")
                );
                if (page.Reason != null)
                    text.Append(' ').Append(page.Reason.English());
                if (page.Gated)
                    text.Append("\ngate: ").Append(page.Gate.English("; "));
                foreach (var group in page.Groups)
                {
                    var entries = group.Entries.Where(e => Matches(e, filter)).ToList();
                    if (entries.Count == 0)
                        continue;
                    var heading = group.Heading?.English() ?? group.Name;
                    text.Append('\n').Append(Ids.Brief(group.Id)).Append(" (").Append(heading).Append(')');
                    if (group.Summary != null)
                        text.Append(' ').Append(group.Summary.English());
                    foreach (var entry in entries)
                    {
                        // As the panel shows it: a pet the player hasn't found has no name while the guard is on.
                        if (page.Id == SetupPage.Pets && Spoilers.Current.PetHidden(entry.Id))
                        {
                            text.Append('\n').Append("  ").Append(Spoilers.Mask).Append(" hidden");
                            continue;
                        }
                        text.Append(
                            CultureInfo.InvariantCulture,
                            $"\n  {Ids.Brief(entry.Id)} {entry.Value}/{entry.Max} was={entry.SaveValue}"
                        );
                        if (entry.Modifiers.Count > 0)
                            text.Append(CultureInfo.InvariantCulture, $" modifiers={entry.Modifiers.Count}");
                        if (page.Unmet(entry, e => e.Value) is { Count: > 0 } needs)
                            text.Append(" needs: ").Append(needs.English("; "));
                        if (entry.Fixed != null)
                            text.Append(" fixed: ").Append(entry.Fixed.English());
                    }
                }
                return text.ToString();
            }
        );
    }

    private static bool Matches(PageEntry entry, string? filter) =>
        filter is null or "all"
        || entry.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string State(SetupPage page) =>
        page.State switch
        {
            PageState.ReadOnly => "read-only",
            PageState.NoData => "no-data",
            _ => page.Gated ? "gated" : "changeable",
        };

    private static SideState Side(string? word) =>
        word?.ToLowerInvariant() switch
        {
            null or "a" => PanelState.A,
            "b" => PanelState.B ?? throw new CommandException("there is no side B (see cs2 ui)"),
            _ => throw new CommandException($"'{word}': name the side, a or b"),
        };

    private static void Read(CommandCall call, SideState side, Func<IReadOnlyList<SetupPage>, string> text)
    {
        if (!side.CanChange)
            throw new CommandException("a saved run's setup can't be rebuilt: it has no pages");
        RunService.CheckSide(
            side.ToSetup(""),
            side.Changes,
            (check, error) =>
            {
                if (check == null)
                    call.Error(error?.Message ?? "the check failed");
                else
                    call.Ok(text(check.Pages));
            }
        );
    }
}
