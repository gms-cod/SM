using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для BlacklistManager: сопоставление записей с правилами чёрного списка по различным критериям,
/// вычисление SHA256, добавление/удаление правил.
/// </summary>
public class BlacklistManagerTests : IDisposable
{
    /// <summary>Путь к временному файлу конфига для тестов.</summary>
    private readonly string _testFilePath;
    /// <summary>Хранилище данных JsonDataStore для тестов.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Экземпляр BlacklistManager, привязанный к временному хранилищу.</summary>
    private readonly BlacklistManager _manager;

    public BlacklistManagerTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        _store = new JsonDataStore(_testFilePath);
        _manager = new BlacklistManager(_store);
    }

    /// <summary>
    /// Проверяет точное совпадение пути файла — базовый сценарий.
    /// </summary>
    [Fact]
    public void MatchEntry_ExactPath_Success()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\BadApp\virus.exe",
            MatchType = BlacklistMatchType.Path
        };
        AddRule(rule);

        var entry = new StartupEntry
        {
            FilePath = @"C:\BadApp\virus.exe"
        };

        var match = _manager.MatchEntry(entry);
        Assert.NotNull(match);
        Assert.Equal(rule.Id, match.Id);
    }

    /// <summary>
    /// Проверяет, что сравнение путей регистронезависимо.
    /// </summary>
    [Fact]
    public void MatchEntry_ExactPath_CaseInsensitive()
    {
        AddRule(new BlacklistRule
        {
            Value = @"C:\BadApp\virus.exe",
            MatchType = BlacklistMatchType.Path
        });

        var entry = new StartupEntry
        {
            FilePath = @"c:\badapp\VIRUS.EXE"
        };

        Assert.NotNull(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет, что при несовпадении пути возвращается null.
    /// </summary>
    [Fact]
    public void MatchEntry_PathMismatch_ReturnsNull()
    {
        AddRule(new BlacklistRule
        {
            Value = @"C:\BadApp\virus.exe",
            MatchType = BlacklistMatchType.Path
        });

        var entry = new StartupEntry
        {
            FilePath = @"C:\GoodApp\clean.exe"
        };

        Assert.Null(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет сопоставление по wildcard-шаблону пути.
    /// </summary>
    [Fact]
    public void MatchEntry_Wildcard_Success()
    {
        AddRule(new BlacklistRule
        {
            Value = @"C:\Program Files\*\update.exe",
            MatchType = BlacklistMatchType.PathWildcard
        });

        var entry = new StartupEntry
        {
            FilePath = @"C:\Program Files\SomeApp\update.exe"
        };

        Assert.NotNull(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет wildcard-шаблон с глубоким путём.
    /// </summary>
    [Fact]
    public void MatchEntry_Wildcard_DeepPath()
    {
        AddRule(new BlacklistRule
        {
            Value = @"*\AppData\*\update.exe",
            MatchType = BlacklistMatchType.PathWildcard
        });

        var entry = new StartupEntry
        {
            FilePath = @"C:\Users\User\AppData\Local\App\update.exe"
        };

        Assert.NotNull(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет, что wildcard не совпадает с неподходящим файлом.
    /// </summary>
    [Fact]
    public void MatchEntry_Wildcard_NoMatch()
    {
        AddRule(new BlacklistRule
        {
            Value = @"C:\Program Files\*\update.exe",
            MatchType = BlacklistMatchType.PathWildcard
        });

        var entry = new StartupEntry
        {
            FilePath = @"C:\Program Files\App\main.exe"
        };

        Assert.Null(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет сопоставление по издателю.
    /// </summary>
    [Fact]
    public void MatchEntry_Publisher_Success()
    {
        AddRule(new BlacklistRule
        {
            Value = "Malware Inc",
            MatchType = BlacklistMatchType.Publisher
        });

        var entry = new StartupEntry
        {
            Publisher = "Malware Inc",
            FilePath = @"C:\Any\path.exe"
        };

        Assert.NotNull(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет, что сравнение по издателю регистронезависимо.
    /// </summary>
    [Fact]
    public void MatchEntry_Publisher_CaseInsensitive()
    {
        AddRule(new BlacklistRule
        {
            Value = "MALWARE INC",
            MatchType = BlacklistMatchType.Publisher
        });

        var entry = new StartupEntry { Publisher = "malware inc" };

        Assert.NotNull(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет сопоставление по SHA256-хешу файла.
    /// </summary>
    [Fact]
    public void MatchEntry_SHA256_Success()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "test content for hash");
            var hash = BlacklistManager.ComputeSha256(testFile);

            AddRule(new BlacklistRule
            {
                Value = hash,
                MatchType = BlacklistMatchType.SHA256
            });

            var entry = new StartupEntry { FilePath = testFile };
            entry.SHA256 = BlacklistManager.ComputeSha256(testFile);

            Assert.NotNull(_manager.MatchEntry(entry));
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    /// <summary>
    /// Проверяет, что при пустом чёрном списке MatchEntry возвращает null.
    /// </summary>
    [Fact]
    public void MatchEntry_EmptyBlacklist_ReturnsNull()
    {
        var entry = new StartupEntry
        {
            FilePath = @"C:\Any\app.exe"
        };

        Assert.Null(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет, что при нескольких правилах побеждает первое подходящее.
    /// </summary>
    [Fact]
    public void MatchEntry_MultipleRules_FirstMatchWins()
    {
        AddRule(new BlacklistRule
        {
            Value = @"C:\FirstMatch\app.exe",
            MatchType = BlacklistMatchType.Path
        });
        AddRule(new BlacklistRule
        {
            Value = @"C:\SecondMatch\app.exe",
            MatchType = BlacklistMatchType.Path
        });

        var entry = new StartupEntry { FilePath = @"C:\SecondMatch\app.exe" };
        var match = _manager.MatchEntry(entry);

        Assert.NotNull(match);
        Assert.Equal(@"C:\SecondMatch\app.exe", match.Value);
    }

    /// <summary>
    /// Проверяет, что ComputeSha256 возвращает пустую строку для несуществующего файла.
    /// </summary>
    [Fact]
    public void ComputeSha256_NonExistentFile_ReturnsEmpty()
    {
        var hash = BlacklistManager.ComputeSha256(@"C:\NonExistent\file.exe");
        Assert.Empty(hash);
    }

    /// <summary>
    /// Проверяет, что ComputeSha256 возвращает 64-символьный hex-хеш для существующего файла.
    /// </summary>
    [Fact]
    public void ComputeSha256_ValidFile_ReturnsHash()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(testFile, "hello");
            var hash = BlacklistManager.ComputeSha256(testFile);

            Assert.NotEmpty(hash);
            Assert.Equal(64, hash.Length);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    /// <summary>Добавляет правило в чёрный список.</summary>
    private void AddRule(BlacklistRule rule)
    {
        var config = _store.Load();
        config.Blacklist.Add(rule);
        _store.Save(config);
    }

    /// <summary>Удаляет правило из чёрного списка по ID.</summary>
    private void RemoveRule(string ruleId)
    {
        var config = _store.Load();
        config.Blacklist.RemoveAll(r => r.Id == ruleId);
        _store.Save(config);
    }

    /// <summary>
    /// Проверяет, что после удаления правила запись больше не блокируется.
    /// </summary>
    [Fact]
    public void MatchEntry_AfterRuleRemoved_ReturnsNull()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\BadApp\virus.exe",
            MatchType = BlacklistMatchType.Path
        };
        AddRule(rule);

        var entry = new StartupEntry
        {
            FilePath = @"C:\BadApp\virus.exe"
        };

        Assert.NotNull(_manager.MatchEntry(entry));

        RemoveRule(rule.Id);

        Assert.Null(_manager.MatchEntry(entry));
    }

    /// <summary>
    /// Проверяет, что удаление одного правила не влияет на блокировку по другим правилам.
    /// </summary>
    [Fact]
    public void MatchEntry_UnblockViaRuleRemoval_OnlyAffectsTarget()
    {
        var rule1 = new BlacklistRule
        {
            Value = @"C:\Malware\bad.exe",
            MatchType = BlacklistMatchType.Path
        };
        var rule2 = new BlacklistRule
        {
            Value = @"C:\Tools\good.exe",
            MatchType = BlacklistMatchType.Path
        };
        AddRule(rule1);
        AddRule(rule2);

        var malEntry = new StartupEntry { FilePath = @"C:\Malware\bad.exe" };
        var toolEntry = new StartupEntry { FilePath = @"C:\Tools\good.exe" };

        Assert.NotNull(_manager.MatchEntry(malEntry));
        Assert.NotNull(_manager.MatchEntry(toolEntry));

        // Unblock только rule1
        RemoveRule(rule1.Id);

        Assert.Null(_manager.MatchEntry(malEntry));
        Assert.NotNull(_manager.MatchEntry(toolEntry));
    }

    /// <summary>
    /// Проверяет, что удаление правила по издателю снимает блокировку.
    /// </summary>
    [Fact]
    public void MatchEntry_UnblockPublisher_RemovesBlock()
    {
        var rule = new BlacklistRule
        {
            Value = "BadPublisher",
            MatchType = BlacklistMatchType.Publisher
        };
        AddRule(rule);

        var entry = new StartupEntry
        {
            Publisher = "BadPublisher",
            FilePath = @"C:\Any\app.exe"
        };

        Assert.NotNull(_manager.MatchEntry(entry));

        RemoveRule(rule.Id);

        Assert.Null(_manager.MatchEntry(entry));
    }

    // ───── MatchesFilePath (сопоставление пути процесса с правилом ЧС) ─────

    /// <summary>
    /// Проверяет точное совпадение пути файла (используется для процессов вне списка записей).
    /// </summary>
    [Fact]
    public void MatchesFilePath_ExactPath_True()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\Users\gms\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe",
            MatchType = BlacklistMatchType.Path
        };
        Assert.True(BlacklistManager.MatchesFilePath(rule, @"C:\Users\GMS\WindowsApps\MSTeams_8wekyb3d8bbwe\ms-teams.exe"));
        Assert.False(BlacklistManager.MatchesFilePath(rule, @"C:\Users\Other\WindowsApps\ms-teams.exe"));
    }

    /// <summary>
    /// Проверяет совпадение по wildcard-маске пути.
    /// </summary>
    [Fact]
    public void MatchesFilePath_Wildcard_True()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\Program Files\WindowsApps\MSTeams_*\ms-teams.exe",
            MatchType = BlacklistMatchType.PathWildcard
        };
        Assert.True(BlacklistManager.MatchesFilePath(rule,
            @"C:\Program Files\WindowsApps\MSTeams_26198.304.4946.9672_x64__8wekyb3d8bbwe\ms-teams.exe"));
        Assert.False(BlacklistManager.MatchesFilePath(rule, @"C:\Somewhere\else\ms-teams.exe"));
    }

    /// <summary>
    /// Проверяет, что MatchesFilePath не матчит правила по издателю или SHA256.
    /// </summary>
    [Fact]
    public void MatchesFilePath_OtherMatchTypes_False()
    {
        Assert.False(BlacklistManager.MatchesFilePath(
            new BlacklistRule { Value = "BadPublisher", MatchType = BlacklistMatchType.Publisher },
            @"C:\Any\app.exe"));
        Assert.False(BlacklistManager.MatchesFilePath(
            new BlacklistRule { Value = "abc123", MatchType = BlacklistMatchType.SHA256 },
            @"C:\Any\app.exe"));
    }

    /// <summary>
    /// Проверяет, что некорректные символы в маске не вызывают необработанных исключений и корректно отклоняются.
    /// </summary>
    [Fact]
    public void MatchesFilePath_InvalidWildcard_DoesNotThrow()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\[invalid(regex**pattern\*.exe",
            MatchType = BlacklistMatchType.PathWildcard
        };

        // Не должно выбрасывать RegexParseException
        var result = BlacklistManager.MatchesFilePath(rule, @"C:\test\app.exe");
        Assert.False(result);
    }

    /// <summary>
    /// Проверяет, что пустой путь или пустая маска возвращают false без ошибок.
    /// </summary>
    [Fact]
    public void MatchesFilePath_EmptyOrNull_ReturnsFalse()
    {
        var rule = new BlacklistRule
        {
            Value = "",
            MatchType = BlacklistMatchType.PathWildcard
        };

        Assert.False(BlacklistManager.MatchesFilePath(rule, @"C:\app.exe"));
        Assert.False(BlacklistManager.MatchesFilePath(rule, ""));
    }

    /// <summary>Удаляет временный конфиг после тестов.</summary>
    public void Dispose()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }
}
