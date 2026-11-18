using System.Reflection;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class LinksTests
{
    private static readonly CheckedState Checked = CheckedState.Of("26724394", "0.11.6-alpha.1", "0.11.6-alpha.1");

    // Every address a player can open is a whole https link, or empty to leave its row switched off.
    [Fact]
    public void EveryLinkIsAnHttpsAddressOrEmpty()
    {
        var addresses = typeof(Links)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(ReportLinks).GetMembers(BindingFlags.Public | BindingFlags.Static))
            .Select(m =>
                m switch
                {
                    FieldInfo f when f.FieldType == typeof(string) => (
                        Name: m.Name,
                        Address: (string?)f.GetValue(null)
                    ),
                    PropertyInfo p when p.PropertyType == typeof(string) => (
                        Name: m.Name,
                        Address: (string?)p.GetValue(null)
                    ),
                    _ => (Name: m.Name, Address: null),
                }
            )
            .Where(a =>
                a.Address != null
                && a.Name
                    is not (
                        nameof(ReportLinks.Repository)
                        or nameof(ReportLinks.IssueForm)
                        or nameof(Links.FeedbackForm)
                    )
            )
            .Append((Name: "Feedback", Address: Links.Feedback("0.1.0-preview.1", Checked)))
            .ToList();
        Assert.Contains(addresses, a => a.Name == nameof(ReportLinks.DirectMessage));
        foreach (var (name, address) in addresses)
        {
            if (address!.Length == 0)
                continue;
            Assert.True(
                Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"{name}: {address}"
            );
        }
    }

    [Fact]
    public void FeedbackCarriesTheVersionsEscaped()
    {
        Assert.Equal(
            "https://github.com/Crashv0/CombatSimulator2/issues/new?template=feedback.yml"
                + "&cs2-version=0.1.0-preview.1&game-version=0.11.6-alpha.1%20%28build%2026724394%29",
            Links.Feedback("0.1.0-preview.1", Checked)
        );
    }

    [Fact]
    public void FeedbackSaysWhenTheGameWasNotChecked()
    {
        var link = Links.Feedback("0.1.0", CheckedState.Of("1", null, "0.12.0-alpha"));
        Assert.EndsWith(
            "&game-version=0.12.0-alpha%20%28build%201%29%2C%20not%20checked",
            link,
            StringComparison.Ordinal
        );
        var unknown = Links.Feedback("0.1.0", CheckedState.Of(CheckedState.UnknownBuild, null, "0.12.0-alpha"));
        Assert.EndsWith("%2C%20build%20unknown", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscordIsTheReportChannel() => Assert.Equal(ReportLinks.DiscordChannel, Links.Discord);
}
