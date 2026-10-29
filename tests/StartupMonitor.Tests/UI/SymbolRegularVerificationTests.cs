using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace StartupMonitor.Tests.UI;

public class SymbolRegularVerificationTests
{
    [Fact]
    public void AllXamlSymbols_MustExistInWpfUiSymbolRegular()
    {
        var baseDir = AppContext.BaseDirectory;
        // Search upwards for solution root
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StartupMonitor.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var uiDir = Path.Combine(dir.FullName, "src", "StartupMonitor.UI");
        Assert.True(Directory.Exists(uiDir), $"UI directory not found: {uiDir}");

        var regex = new Regex(@"(?:Symbol=""([A-Za-z0-9]+)""|\{ui:SymbolIcon\s+([A-Za-z0-9]+)\})");
        var xamlFiles = Directory.GetFiles(uiDir, "*.xaml", SearchOption.AllDirectories);

        var failures = new List<string>();

        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            var matches = regex.Matches(text);
            foreach (Match m in matches)
            {
                var symName = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
                if (!Enum.TryParse(typeof(Wpf.Ui.Controls.SymbolRegular), symName, out _))
                {
                    failures.Add($"{Path.GetFileName(file)}: '{symName}' is not a valid SymbolRegular");
                }
            }
        }

        Assert.True(failures.Count == 0, $"Invalid symbols found:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void AllXamlAlignments_MustBeValid()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StartupMonitor.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var uiDir = Path.Combine(dir.FullName, "src", "StartupMonitor.UI");
        var xamlFiles = Directory.GetFiles(uiDir, "*.xaml", SearchOption.AllDirectories);
        var failures = new List<string>();

        var hRegex = new Regex(@"HorizontalAlignment=""([^""]+)""");
        var vRegex = new Regex(@"VerticalAlignment=""([^""]+)""");

        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in hRegex.Matches(text))
            {
                var val = m.Groups[1].Value;
                if (!val.StartsWith("{") && !Enum.TryParse(typeof(System.Windows.HorizontalAlignment), val, out _))
                {
                    failures.Add($"{Path.GetFileName(file)}: '{val}' is not a valid HorizontalAlignment");
                }
            }
            foreach (Match m in vRegex.Matches(text))
            {
                var val = m.Groups[1].Value;
                if (!val.StartsWith("{") && !Enum.TryParse(typeof(System.Windows.VerticalAlignment), val, out _))
                {
                    failures.Add($"{Path.GetFileName(file)}: '{val}' is not a valid VerticalAlignment");
                }
            }
        }

        Assert.True(failures.Count == 0, $"Invalid alignments found:\n{string.Join("\n", failures)}");
    }
}