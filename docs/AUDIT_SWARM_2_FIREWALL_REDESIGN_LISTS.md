# Аудит и проект глубокого редизайна страницы Firewall (NetworkSecurityHubView)

**Версия:** v1.0.0  
**Статус:** Проект (макеты кода)  
**Автор:** Ведущий WPF/XAML архитектор, StartupMonitor  
**Целевой файл:** `src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml` + `.xaml.cs`

---

## 1. Исполнительное резюме

Текущая страница Firewall (`NetworkSecurityHubView`) функционально покрывает статус брандмауэра, режимы фильтрации, активные соединения, правила Windows Firewall, Port Defender и журнал банов. Однако внешний вид не соответствует неоновому кибер-стилю главной страницы: отсутствуют светящиеся акценты, скругления `CornerRadius="8"`, мягкие тени и полупрозрачные фоны. Интерфейс перегружен одной страницей без чёткого разделения ответственности между вкладками.

**Решение:** глубокий редизайн с 5 логически изолированными вкладками в едином неоновом кибер-стиле, отдельные таблицы для Белого и Чёрного списка с синхронизацией через `AppSettings` и Named Pipe, а также новые `PipeClient` методы для управления списками приложений.

---

## 2. Аудит текущего дизайна (NetworkSecurityHubView.xaml)

### 2.1 Что хорошо

| Аспект | Оценка | Комментарий |
|--------|--------|-------------|
| Статус брандмауэра и бейджи профилей | ✅ | Информативные бейджи, чёткая визуализация вкл/выкл |
| Режимы фильтрации (Normal / Whitelist / Killswitch) | ✅ | Радио-кнопки с защитой от ошибочного переключения Killswitch |
| Живые соединения (Live) | ✅ | Таблица с GeoIP, фильтрами WAN/LAN, контекстным меню |
| Правила брандмауэра | ✅ | Полный CRUD, пресеты безопасности, экспорт/импорт |
| Port Defender | ✅ | Статус, защищаемые порты, настройка через окно |
| Журнал банов | ✅ | Обратный отсчёт, разблокировка IP, сброс всех банов |
| Zero-Crash обработка ошибок | ✅ | Все вызовы в try-catch, ошибки в статусную строку |

### 2.2 Проблемы текущего дизайна

| № | Проблема | Последствие |
|---|----------|-------------|
| 1 | Нет неонового кибер-стиля (светящиеся акценты, тени, полупрозрачность) | Страница визуально выпадает из общего облика главного окна |
| 2 | `CornerRadius` не unified (4, 8, 10) | Несогласованный визуальный шум |
| 3 | Все вкладки на одной странице без чёткого разделения | Перегрузка интерфейса, сложная навигация |
| 4 | Нет отдельных вкладок Белого и Чёрного списка приложений | Пользователь не может визуально управлять списками разрешений/запретов по приложениям |
| 5 | Белый/чёрный список приложений (`FirewallWhitelistedApps` / `FirewallBlacklistedApps` в `AppSettings`) не отображается в UI | Данные есть в модели, но недоступны пользователю |
| 6 | Нет Named Pipe методов для синхронизации белого/чёрного списка приложений со службой | Нет механизма сквозной синхронизации UI ↔ AppSettings ↔ Service |
| 7 | Цвета захардкожены (`#107C41`, `#D83B01`, `#20107C41`) | Не поддерживаются темы оформления (dark/light) |

---

## 3. Концепция редизайна: неоновый кибер-стиль

### 3.1 Дизайн-токены (XAML Resources)

```xml
<!-- Акцентный неоновый цвет (циан-неон) -->
<Color x:Key="NeonAccent">#00F0FF</Color>
<Color x:Key="NeonAccentGlow">#00F0FF33</Color>
<Color x:Key="NeonDanger">#FF0055</Color>
<Color x:Key="NeonSuccess">#00FF88</Color>
<Color x:Key="NeonWarning">#FFB800</Color>

<!-- Полупрозрачные фоны карточек -->
<Color x:Key="CardGlassBg">#1A1A2E</Color>   <!-- тёмное стекло -->
<Color x:Key="CardGlassBorder">#00F0FF44</Color>

<!-- Скругления -->
<CornerRadius x:Key="CardCornerRadius">8</CornerRadius>
<CornerRadius x:Key="BadgeCornerRadius">6</CornerRadius>
<CornerRadius x:Key="ButtonCornerRadius">6</CornerRadius>

<!-- Тени -->
<DropShadowEffect x:Key="NeonGlowShadow" Color="{StaticResource NeonAccent}" BlurRadius="12" Opacity="0.35" ShadowDepth="0" Direction="0"/>
<DropShadowEffect x:Key="CardSoftShadow" Color="#000000" BlurRadius="8" Opacity="0.25" ShadowDepth="2" Direction="270"/>
```

### 3.2 Визуальные принципы

- **Карточки:** `CornerRadius="8"`, фон `CardBackgroundFillColorDefaultBrush` с неоновой обводкой, мягкая тень `CardSoftShadow`
- **Акценты:** свечение `NeonGlowShadow` вокруг активных элементов, неоновый текст для заголовков вкладок
- **Таблицы:** absence grid lines, чередование строк с полупрозрачным фоном, состояния Allow/Block в виде неоновых бейджей
- **Кнопки:** неоновая подсветка при наведении, `CornerRadius="6"`, минималистичные иконы

---

## 4. Архитектура 5 вкладок

| Вкладка | Ключ | Содержание | Основные действия |
|---------|------|------------|-------------------|
| 1 | `Мониторинг сети` | Таблица активных соединений с индикаторами трафика, статусами Allow/Block, быстрые действия | Блокировка IP, создание правила, копирование, Kill processo |
| 2 | `Белый список` | Таблица приложений с неограниченным сетевым доступом (путь, имя, дата добавления) | Добавить программу, удалить из списка |
| 3 | `Чёрный список` | Таблица заблокированных приложений (полный путь, имя, дата блокировки) | Убрать из списка (снять блокирующее правило) |
| 4 | `Правила брандмауэра` | Все правила Windows Firewall (вкл/выкл, действие, направление, порты) | Создать, редактировать, удалить, экспорт/импорт, пресеты |
| 5 | `Port Defender` | Защита сетевых портов, список защищаемых портов, статистика атак | Настройка, обновление, управление баном |

---

## 5. Модели данных

### 5.1 FirewallWhitelistRow (UI-обёртка для белого списка)

```csharp
public sealed class FirewallWhitelistRow : INotifyPropertyChanged
{
    public string FullPath { get; set; } = string.Empty;
    public string ProcessName => Path.GetFileName(FullPath);
    public string ShortName => Path.GetFileNameWithoutExtension(FullPath);
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string AddedAtText => AddedAt.ToLocalTime().ToString("g");
    public bool IsSystem => FullPath.StartsWith(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

### 5.2 FirewallBlacklistRow (UI-обёртка для чёрного списка)

```csharp
public sealed class FirewallBlacklistRow : INotifyPropertyChanged
{
    public string FullPath { get; set; } = string.Empty;
    public string ProcessName => Path.GetFileName(FullPath);
    public string ShortName => Path.GetFileNameWithoutExtension(FullPath);
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    public string BlockedAtText => BlockedAt.ToLocalTime().ToString("g");
    public string Reason { get; set; } = string.Empty;
    public bool IsSystem => FullPath.StartsWith(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

### 5.3 LiveConnectionRow — дополнения для вкладки 1

Дополнительные свойства для индикатора трафика:
- `TrafficBadge` — "↑↓" с цветом в зависимости от состояния
- `FirewallBadgeBrush` — кисть для бейджа брандмауэра (зелёный / красный)

---

## 6. Новые типы сообщений Named Pipe (PipeMessage.cs)

Нужно добавить в `PipeMessageTypes`:

```csharp
public const string GetFirewallWhitelist = "GetFirewallWhitelist";
public const string GetFirewallWhitelistResponse = "GetFirewallWhitelistResponse";
public const string AddToFirewallWhitelist = "AddToFirewallWhitelist";
public const string RemoveFromFirewallWhitelist = "RemoveFromFirewallWhitelist";
public const string GetFirewallBlacklist = "GetFirewallBlacklist";
public const string GetFirewallBlacklistResponse = "GetFirewallBlacklistResponse";
public const string AddToFirewallBlacklist = "AddToFirewallBlacklist";
public const string RemoveFromFirewallBlacklist = "RemoveFromFirewallBlacklist";
```

Полезные нагрузки:

```csharp
public record FirewallAppListPayload(List<string> Paths);
public record FirewallAppActionPayload(string Path, string? Reason = null);
```

---

## 7. Новые методы PipeClient.cs

```csharp
/// <summary>Запрашивает белый список сетевых приложений.</summary>
public async Task<List<string>> GetFirewallWhitelistAsync(CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetFirewallWhitelist }, ct);
    if (response?.Type == PipeMessageTypes.GetFirewallWhitelistResponse && !string.IsNullOrWhiteSpace(response.Payload))
    {
        try { return JsonSerializer.Deserialize<List<string>>(response.Payload, JsonOptions) ?? new(); }
        catch { }
    }
    return new();
}

/// <summary>Добавляет приложение в белый список.</summary>
public async Task<bool> AddToFirewallWhitelistAsync(string path, CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage
    {
        Type = PipeMessageTypes.AddToFirewallWhitelist,
        Payload = JsonSerializer.Serialize(new FirewallAppActionPayload(path), JsonOptions)
    }, ct);
    return response?.Type == PipeMessageTypes.Success;
}

/// <summary>Удаляет приложение из белого списка.</summary>
public async Task<bool> RemoveFromFirewallWhitelistAsync(string path, CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage
    {
        Type = PipeMessageTypes.RemoveFromFirewallWhitelist,
        Payload = JsonSerializer.Serialize(new FirewallAppActionPayload(path), JsonOptions)
    }, ct);
    return response?.Type == PipeMessageTypes.Success;
}

/// <summary>Запрашивает чёрный список сетевых приложений.</summary>
public async Task<List<string>> GetFirewallBlacklistAsync(CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetFirewallBlacklist }, ct);
    if (response?.Type == PipeMessageTypes.GetFirewallBlacklistResponse && !string.IsNullOrWhiteSpace(response.Payload))
    {
        try { return JsonSerializer.Deserialize<List<string>>(response.Payload, JsonOptions) ?? new(); }
        catch { }
    }
    return new();
}

/// <summary>Добавляет приложение в чёрный список.</summary>
public async Task<bool> AddToFirewallBlacklistAsync(string path, string? reason = null, CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage
    {
        Type = PipeMessageTypes.AddToFirewallBlacklist,
        Payload = JsonSerializer.Serialize(new FirewallAppActionPayload(path, reason), JsonOptions)
    }, ct);
    return response?.Type == PipeMessageTypes.Success;
}

/// <summary>Удаляет приложение из чёрного списка.</summary>
public async Task<bool> RemoveFromFirewallBlacklistAsync(string path, CancellationToken ct = default)
{
    var response = await SendAsync(new PipeMessage
    {
        Type = PipeMessageTypes.RemoveFromFirewallBlacklist,
        Payload = JsonSerializer.Serialize(new FirewallAppActionPayload(path), JsonOptions)
    }, ct);
    return response?.Type == PipeMessageTypes.Success;
}
```

---

## 8. Полная XAML разметка (NetworkSecurityHubView.xaml)

```xml
<UserControl
    x:Class="StartupMonitor.UI.Views.NetworkSecurityHubView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Background="{ui:ThemeResource ApplicationBackgroundBrush}">

    <UserControl.Resources>
        <!-- ===== Дизайн-токены неонового кибер-стиля ===== -->
        <Color x:Key="NeonAccent">#00F0FF</Color>
        <Color x:Key="NeonAccentGlow">#00F0FF44</Color>
        <Color x:Key="NeonDanger">#FF0055</Color>
        <Color x:Key="NeonDangerGlow">#FF005544</Color>
        <Color x:Key="NeonSuccess">#00FF88</Color>
        <Color x:Key="NeonSuccessGlow">#00FF8844</Color>
        <Color x:Key="NeonWarning">#FFB800</Color>
        <Color x:Key="NeonWarningGlow">#FFB80044</Color>

        <SolidColorBrush x:Key="NeonAccentBrush" Color="{StaticResource NeonAccent}"/>
        <SolidColorBrush x:Key="NeonDangerBrush" Color="{StaticResource NeonDanger}"/>
        <SolidColorBrush x:Key="NeonSuccessBrush" Color="{StaticResource NeonSuccess}"/>

        <CornerRadius x:Key="CardCornerRadius">8</CornerRadius>
        <CornerRadius x:Key="BadgeCornerRadius">6</CornerRadius>
        <CornerRadius x:Key="ButtonCornerRadius">6</CornerRadius>

        <DropShadowEffect x:Key="NeonGlowShadow"
                          Color="{StaticResource NeonAccent}"
                          BlurRadius="14" Opacity="0.45" ShadowDepth="0" Direction="0"/>
        <DropShadowEffect x:Key="NeonDangerShadow"
                          Color="{StaticResource NeonDanger}"
                          BlurRadius="12" Opacity="0.40" ShadowDepth="0" Direction="0"/>
        <DropShadowEffect x:Key="CardSoftShadow"
                          Color="#000000" BlurRadius="10" Opacity="0.30" ShadowDepth="3" Direction="270"/>

        <Style x:Key="NeonTabItemStyle" TargetType="TabItem">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="16,10"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Foreground" Value="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
            <Setter Property="Template">
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="Border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{StaticResource CardCornerRadius}"
                            Padding="{TemplateBinding Padding}"
                            Effect="{StaticResource CardSoftShadow}">
                        <ContentPresenter x:Name="ContentSource"
                                          ContentSource="Header"
                                          RecognizesAccessKey="True"
                                          VerticalAlignment="Center"
                                          HorizontalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Border" Property="Background" Value="{StaticResource NeonAccentGlow}"/>
                            <Setter Property="Foreground" Value="{StaticResource NeonAccentBrush}"/>
                            <Setter Property="Effect" Value="{StaticResource NeonGlowShadow}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Border" Property="Background" Value="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter>
        </Style>

        <Style x:Key="NeonDataGridStyle" TargetType="DataGrid">
            <Setter Property="Background" Value="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource NeonAccentBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="GridLinesVisibility" Value="None"/>
            <Setter Property="HeadersVisibility" Value="Column"/>
            <Setter Property="RowHeight" Value="40"/>
            <Setter Property="CanUserAddRows" Value="False"/>
            <Setter Property="CanUserDeleteRows" Value="False"/>
            <Setter Property="CanUserReorderColumns" Value="False"/>
            <Setter Property="IsReadOnly" Value="True"/>
            <Setter Property="SelectionMode" Value="Single"/>
            <Setter Property="HeadersVisibility" Value="Column"/>
            <Setter Property="CellStyle">
                <Setter.Value>
                    <Style TargetType="DataGridCell">
                        <Setter Property="BorderThickness" Value="0"/>
                        <Setter Property="Background" Value="Transparent"/>
                        <Setter Property="Padding" Value="10,6"/>
                        <Setter Property="VerticalAlignment" Value="Center"/>
                        <Setter Property="Template">
                            <ControlTemplate TargetType="DataGridCell">
                                <Border Background="{TemplateBinding Background}"
                                        BorderBrush="{TemplateBinding BorderBrush}"
                                        BorderThickness="{TemplateBinding BorderThickness}"
                                        CornerRadius="{StaticResource CardCornerRadius}"
                                        Padding="{TemplateBinding Padding}">
                                    <ContentPresenter VerticalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Setter>
                        <Style.Triggers>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter Property="Background" Value="{StaticResource NeonAccentGlow}"/>
                                <Setter Property="Foreground" Value="{StaticResource NeonAccentBrush}"/>
                                <Setter Property="Effect" Value="{StaticResource NeonGlowShadow}"/>
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                </Setter.Value>
            </Setter>
            <Setter Property="ColumnHeaderStyle">
                <Setter.Value>
                    <Style TargetType="DataGridColumnHeader">
                        <Setter Property="Background" Value="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}"/>
                        <Setter Property="Foreground" Value="{StaticResource NeonAccentBrush}"/>
                        <Setter Property="FontWeight" Value="SemiBold"/>
                        <Setter Property="FontSize" Value="12"/>
                        <Setter Property="Padding" Value="10,8"/>
                        <Setter Property="BorderThickness" Value="0,0,0,1"/>
                        <Setter Property="BorderBrush" Value="{StaticResource NeonAccentBrush}"/>
                    </Style>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="NeonButtonStyle" TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}">
            <Setter Property="CornerRadius" Value="{StaticResource ButtonCornerRadius}"/>
            <Setter Property="Padding" Value="14,8"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Margin" Value="0,0,8,0"/>
        </Style>

        <Style x:Key="NeonDangerButtonStyle" TargetType="ui:Button" BasedOn="{StaticResource NeonButtonStyle}">
            <Setter Property="Appearance" Value="Danger"/>
        </Style>

        <Style x:Key="GlassCardStyle" TargetType="ui:Card">
            <Setter Property="CornerRadius" Value="{StaticResource CardCornerRadius}"/>
            <Setter Property="Padding" Value="16"/>
            <Setter Property="Margin" Value="0,0,0,12"/>
            <Setter Property="Effect" Value="{StaticResource CardSoftShadow}"/>
        </Style>

        <DataTemplate x:Key="AllowBadgeTemplate">
            <Border Background="{StaticResource NeonSuccessGlow}"
                    BorderBrush="{StaticResource NeonSuccessBrush}"
                    BorderThickness="1"
                    CornerRadius="{StaticResource BadgeCornerRadius}"
                    Padding="8,3"
                    HorizontalAlignment="Left"
                    VerticalAlignment="Center">
                <TextBlock Text="Allow" FontSize="11" FontWeight="SemiBold" Foreground="White"/>
            </Border>
        </DataTemplate>

        <DataTemplate x:Key="BlockBadgeTemplate">
            <Border Background="{StaticResource NeonDangerGlow}"
                    BorderBrush="{StaticResource NeonDangerBrush}"
                    BorderThickness="1"
                    CornerRadius="{StaticResource BadgeCornerRadius}"
                    Padding="8,3"
                    HorizontalAlignment="Left"
                    VerticalAlignment="Center">
                <TextBlock Text="Block" FontSize="11" FontWeight="SemiBold" Foreground="White"/>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- ===== Шапка: неоновый заголовок + статус +快速 действия ===== -->
        <Grid Grid.Row="0" Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <Border Grid.Column="0" Width="52" Height="52" CornerRadius="{StaticResource CardCornerRadius}"
                    Background="{StaticResource NeonAccentGlow}" Margin="0,0,16,0"
                    VerticalAlignment="Center" Effect="{StaticResource NeonGlowShadow}">
                <ui:SymbolIcon Symbol="Shield24" FontSize="24"
                               Foreground="{StaticResource NeonAccentBrush}"
                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>

            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                <TextBlock Text="{Binding FirewallHubTitle, Source={StaticResource Loc}}"
                           FontSize="20" FontWeight="Bold"
                           Foreground="{StaticResource NeonAccentBrush}"
                           Effect="{StaticResource NeonGlowShadow}"/>
                <TextBlock x:Name="TxtHubSubtitle" Text="Центр управления сетевой безопасностью"
                           FontSize="13"
                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                           Margin="0,4,0,0"/>
            </StackPanel>

            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                <ui:Button x:Name="BtnRefreshAll" Appearance="Secondary"
                           Content="{Binding NetBtnRefreshStatus, Source={StaticResource Loc}}"
                           Click="OnRefreshAllClick" Style="{StaticResource NeonButtonStyle}"/>
                <ui:Button x:Name="BtnExportWhitelist" Appearance="Secondary"
                           Icon="{ui:SymbolIcon Save24}"
                           Content="Эксп. белого списка"
                           ToolTip="Экспортировать белый список приложений"
                           Click="OnExportWhitelistClick" Style="{StaticResource NeonButtonStyle}"/>
                <ui:Button x:Name="BtnExportBlacklist" Appearance="Secondary"
                           Icon="{ui:SymbolIcon Save24}"
                           Content="Эксп. чёрного списка"
                           ToolTip="Экспортировать чёрный список приложений"
                           Click="OnExportBlacklistClick" Style="{StaticResource NeonButtonStyle}"/>
            </StackPanel>
        </Grid>

        <!-- ===== Статусная карточка брандмауэра ===== -->
        <ui:Card Grid.Row="1" x:Name="StatusCard" Style="{StaticResource GlassCardStyle}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <Border Grid.Column="0" Width="44" Height="44" CornerRadius="{StaticResource BadgeCornerRadius}"
                        Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}"
                        Margin="0,0,14,0" VerticalAlignment="Center">
                    <ui:SymbolIcon x:Name="FirewallShieldIcon" Symbol="Shield24" FontSize="22"
                                   Foreground="{ui:ThemeResource AccentTextFillColorPrimaryBrush}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>

                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <Border x:Name="FirewallStateBadge" CornerRadius="{StaticResource BadgeCornerRadius}"
                                Padding="8,3" VerticalAlignment="Center" Margin="0,0,8,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtFirewallState"
                                       Text="{Binding NetFwStateUnknown, Source={StaticResource Loc}}"
                                       FontSize="13" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"
                                       VerticalAlignment="Center"/>
                        </Border>
                        <TextBlock x:Name="TxtFirewallDetails" Text="{Binding NetStatusLoading, Source={StaticResource Loc}}"
                                   FontSize="12" Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                   VerticalAlignment="Center"/>
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <Border x:Name="ProfileDomainBadge" CornerRadius="{StaticResource BadgeCornerRadius}"
                                Padding="8,3" Margin="0,0,6,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfileDomain" Text="{Binding NetProfileDomainDefault, Source={StaticResource Loc}}"
                                       FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
                        </Border>
                        <Border x:Name="ProfilePrivateBadge" CornerRadius="{StaticResource BadgeCornerRadius}"
                                Padding="8,3" Margin="0,0,6,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfilePrivate" Text="{Binding NetProfilePrivateDefault, Source={StaticResource Loc}}"
                                       FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
                        </Border>
                        <Border x:Name="ProfilePublicBadge" CornerRadius="{StaticResource BadgeCornerRadius}"
                                Padding="8,3"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfilePublic" Text="{Binding NetProfilePublicDefault, Source={StaticResource Loc}}"
                                       FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
                        </Border>
                    </StackPanel>
                </StackPanel>

                <ui:Button x:Name="BtnRefreshStatus" Grid.Column="2" Appearance="Secondary"
                           Content="{Binding NetBtnRefreshStatus, Source={StaticResource Loc}}"
                           Click="OnRefreshStatusClick" VerticalAlignment="Center"
                           Margin="12,0,0,0" Style="{StaticResource NeonButtonStyle}"/>
            </Grid>
        </ui:Card>

        <!-- ===== 5 Вкладок редизайна ===== -->
        <TabControl Grid.Row="2" x:Name="MainTabs"
                    ItemContainerStyle="{StaticResource NeonTabItemStyle}"
                    SelectionChanged="OnMainTabsSelectionChanged">

            <!-- ----------------------------------------------------------------
                 Вкладка 1: Мониторинг сети (Active Connections)
                 ---------------------------------------------------------------- -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="Globe24" FontSize="14" Margin="0,0,6,0"
                                       Foreground="{StaticResource NeonAccentBrush}"/>
                        <TextBlock Text="{Binding NetLiveConnectionsHeader, Source={StaticResource Loc}}"
                                   VerticalAlignment="Center" Foreground="{StaticResource NeonAccentBrush}"/>
                    </StackPanel>
                </TabItem.Header>
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <Grid Grid.Row="0" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*"/>
                            <ColumnDefinition Width="3*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <ui:TextBox x:Name="TxtSearchConnections" Grid.Row="0" Grid.Column="0"
                                     PlaceholderText="{Binding NetSearchConnectionsHint, Source={StaticResource Loc}}"
                                     TextChanged="OnConnectionsSearchChanged" Margin="0,0,12,6"/>

                        <WrapPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,6">
                            <RadioButton x:Name="RbConnAll" GroupName="ConnFilter"
                                         Content="{Binding NetFilterAll, Source={StaticResource Loc}}"
                                         IsChecked="True" Checked="OnConnectionsFilterChanged"
                                         VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="RbConnWan" GroupName="ConnFilter"
                                         Content="{Binding NetFilterWan, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="RbConnLan" GroupName="ConnFilter"
                                         Content="{Binding NetFilterLan, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="RbConnEstablished" GroupName="ConnFilter"
                                         Content="{Binding NetFilterEstablished, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="RbConnListening" GroupName="ConnFilter"
                                         Content="{Binding NetFilterListen, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" VerticalAlignment="Center"/>
                        </WrapPanel>

                        <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2"
                                    Orientation="Horizontal" VerticalAlignment="Center">
                            <CheckBox x:Name="ChkAutoRefreshConn" Content="{Binding NetAutoRefresh2s, Source={StaticResource Loc}}"
                                      IsChecked="True"
                                      Checked="OnAutoRefreshConnChanged" Unchecked="OnAutoRefreshConnChanged"
                                      VerticalAlignment="Center" Margin="0,0,12,0"/>
                            <ui:Button x:Name="BtnRefreshConn" Appearance="Secondary"
                                       Content="{Binding NetBtnRefreshNow, Source={StaticResource Loc}}"
                                       Click="OnRefreshConnClick" Margin="0,0,12,0"
                                       Style="{StaticResource NeonButtonStyle}"/>
                            <TextBlock x:Name="TxtConnectionsSummary" Text="—"
                                       FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center"/>
                        </StackPanel>
                    </Grid>

                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridLiveConnections"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserReorderColumns="False"
                                  IsReadOnly="True"
                                  SelectionMode="Single"
                                  GridLinesVisibility="None"
                                  HeadersVisibility="Column"
                                  BorderThickness="1"
                                  BorderBrush="{StaticResource NeonAccentBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="44"
                                  Style="{StaticResource NeonDataGridStyle}">
                            <DataGrid.ContextMenu>
                                <ContextMenu>
                                    <MenuItem x:Name="CtxBlockRemoteIp" Header="{Binding NetCtxBlockRemoteIp, Source={StaticResource Loc}}" Click="OnCtxBlockRemoteIpClick"/>
                                    <MenuItem x:Name="CtxCreateRuleForProcess" Header="{Binding NetCtxCreateRuleForProcess, Source={StaticResource Loc}}" Click="OnCtxCreateRuleForProcessClick"/>
                                    <Separator/>
                                    <MenuItem x:Name="CtxCopyRemoteIp" Header="{Binding NetCtxCopyRemoteIp, Source={StaticResource Loc}}" Click="OnCtxCopyRemoteIpClick"/>
                                    <MenuItem x:Name="CtxCopyEndpoint" Header="{Binding NetCtxCopyEndpoint, Source={StaticResource Loc}}" Click="OnCtxCopyEndpointClick"/>
                                    <Separator/>
                                    <MenuItem x:Name="CtxWhois" Header="{Binding NetCtxWhois, Source={StaticResource Loc}}" Click="OnCtxWhoisClick"/>
                                    <MenuItem x:Name="CtxVirusTotal" Header="{Binding NetCtxVirusTotal, Source={StaticResource Loc}}" Click="OnCtxVirusTotalClick"/>
                                    <Separator/>
                                    <MenuItem x:Name="CtxKillProcess" Header="{Binding NetCtxKillProcess, Source={StaticResource Loc}}" Click="OnCtxKillProcessClick"/>
                                </ContextMenu>
                            </DataGrid.ContextMenu>
                            <DataGrid.Columns>
                                <DataGridTemplateColumn Header="{Binding NetColProcess, Source={StaticResource Loc}}" Width="1.5*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding ProcessName}" FontSize="12.5" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"
                                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                                                <TextBlock Text="{Binding Pid, StringFormat=' ({0})'}" FontSize="11"
                                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                                           VerticalAlignment="Center"/>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTextColumn Header="{Binding NetColProtocol, Source={StaticResource Loc}}" Binding="{Binding Protocol}" Width="70"/>
                                <DataGridTextColumn Header="{Binding NetColLocalAddress, Source={StaticResource Loc}}" Binding="{Binding LocalEndPoint}" Width="150"/>
                                <DataGridTextColumn Header="{Binding NetColRemoteAddress, Source={StaticResource Loc}}" Binding="{Binding RemoteEndPoint}" Width="160"/>
                                <DataGridTemplateColumn Header="{Binding NetColStatus, Source={StaticResource Loc}}" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border CornerRadius="{StaticResource BadgeCornerRadius}" Padding="6,2"
                                                    Background="{Binding StateBadgeBrush, Converter={StaticResource BrushConverter}}"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding State}" FontSize="11" FontWeight="SemiBold"
                                                           Foreground="White"/>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="{Binding NetColNetworkGeo, Source={StaticResource Loc}}" Width="140">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding CountryFlag}" FontSize="13" VerticalAlignment="Center" Margin="0,0,4,0"/>
                                                <TextBlock Text="{Binding CountryText}" FontSize="12"
                                                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="{Binding NetColFirewall, Source={StaticResource Loc}}" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <ContentControl Content="{Binding FirewallBadge}">
                                                <ContentControl.Style>
                                                    <Style TargetType="ContentControl">
                                                        <Setter Property="ContentTemplate" Value="{StaticResource AllowBadgeTemplate}"/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding FirewallBadge}" Value="🚫 Заблокирован">
                                                                <Setter Property="ContentTemplate" Value="{StaticResource BlockBadgeTemplate}"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </ContentControl.Style>
                                            </ContentControl>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <StackPanel x:Name="PanelConnEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="Globe24" FontSize="40"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,12"
                                           Effect="{StaticResource NeonGlowShadow}"/>
                            <TextBlock Text="{Binding NetNoConnectionsTitle, Source={StaticResource Loc}}" FontSize="15" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       HorizontalAlignment="Center"/>
                            <TextBlock Text="{Binding NetNoConnectionsSubtitle, Source={StaticResource Loc}}" FontSize="13"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,6,0,0"/>
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ----------------------------------------------------------------
                 Вкладка 2: Белый список (Whitelist)
                 ---------------------------------------------------------------- -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="ContactCard24" FontSize="14" Margin="0,0,6,0"
                                       Foreground="{StaticResource NeonSuccessBrush}"/>
                        <TextBlock Text="{Binding NetTabWhitelist, Source={StaticResource Loc}}"
                                   VerticalAlignment="Center" Foreground="{StaticResource NeonSuccessBrush}"/>
                    </StackPanel>
                </TabItem.Header>
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <ui:TextBox x:Name="TxtWhitelistSearch" Grid.Row="0"
                                 PlaceholderText="{Binding NetSearchWhitelistHint, Source={StaticResource Loc}}"
                                 TextChanged="OnWhitelistSearchChanged" Margin="0,0,0,12"/>

                    <WrapPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
                        <ui:Button x:Name="BtnAddWhitelist" Appearance="Primary"
                                   Icon="{ui:SymbolIcon Add24}"
                                   Content="{Binding NetBtnAddToWhitelist, Source={StaticResource Loc}}"
                                   Click="OnAddWhitelistClick" Margin="0,0,8,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnRemoveWhitelist" Appearance="Secondary"
                                   Icon="{ui:SymbolIcon Remove24}"
                                   Content="{Binding NetBtnRemoveFromWhitelist, Source={StaticResource Loc}}"
                                   Click="OnRemoveWhitelistClick" Margin="0,0,8,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnRefreshWhitelist" Appearance="Secondary"
                                   Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                   Click="OnRefreshWhitelistClick" Margin="0,0,0,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <TextBlock x:Name="TxtWhitelistCount" Text="—" FontSize="12"
                                   Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" Margin="12,0,0,4"/>
                    </WrapPanel>

                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridWhitelist"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserReorderColumns="False"
                                  IsReadOnly="True"
                                  SelectionMode="Single"
                                  GridLinesVisibility="None"
                                  HeadersVisibility="Column"
                                  BorderThickness="1"
                                  BorderBrush="{StaticResource NeonSuccessBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="44"
                                  Style="{StaticResource NeonDataGridStyle}">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="{Binding NetColProgram, Source={StaticResource Loc}}"
                                                    Binding="{Binding ProcessName}" Width="2*"/>
                                <DataGridTextColumn Header="{Binding NetColFullPath, Source={StaticResource Loc}}"
                                                    Binding="{Binding FullPath}" Width="3*"/>
                                <DataGridTextColumn Header="{Binding NetColAddedAt, Source={StaticResource Loc}}"
                                                    Binding="{Binding AddedAtText}" Width="180"/>
                                <DataGridTemplateColumn Header="{Binding NetColAction, Source={StaticResource Loc}}" Width="120">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="{StaticResource NeonSuccessGlow}"
                                                    BorderBrush="{StaticResource NeonSuccessBrush}"
                                                    BorderThickness="1"
                                                    CornerRadius="{StaticResource BadgeCornerRadius}"
                                                    Padding="6,2"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="Разрешён" FontSize="11" FontWeight="SemiBold" Foreground="White"/>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <StackPanel x:Name="PanelWhitelistEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="ContactCard24" FontSize="40"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,12"/>
                            <TextBlock Text="{Binding NetNoWhitelistTitle, Source={StaticResource Loc}}" FontSize="15" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       HorizontalAlignment="Center"/>
                            <TextBlock Text="{Binding NetNoWhitelistSubtitle, Source={StaticResource Loc}}" FontSize="13"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,6,0,0"/>
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ----------------------------------------------------------------
                 Вкладка 3: Чёрный список (Blacklist)
                 ---------------------------------------------------------------- -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="BlockContact24" FontSize="14" Margin="0,0,6,0"
                                       Foreground="{StaticResource NeonDangerBrush}"/>
                        <TextBlock Text="{Binding NetTabBlacklist, Source={StaticResource Loc}}"
                                   VerticalAlignment="Center" Foreground="{StaticResource NeonDangerBrush}"/>
                    </StackPanel>
                </TabItem.Header>
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <ui:TextBox x:Name="TxtBlacklistSearch" Grid.Row="0"
                                 PlaceholderText="{Binding NetSearchBlacklistHint, Source={StaticResource Loc}}"
                                 TextChanged="OnBlacklistSearchChanged" Margin="0,0,0,12"/>

                    <WrapPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
                        <ui:Button x:Name="BtnAddBlacklist" Appearance="Secondary"
                                   Icon="{ui:SymbolIcon Add24}"
                                   Content="{Binding NetBtnAddToBlacklist, Source={StaticResource Loc}}"
                                   Click="OnAddBlacklistClick" Margin="0,0,8,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnRemoveBlacklist" Appearance="{StaticResource NeonDangerButtonStyle}"
                                   Icon="{ui:SymbolIcon Remove24}"
                                   Content="{Binding NetBtnRemoveFromBlacklist, Source={StaticResource Loc}}"
                                   Click="OnRemoveBlacklistClick" Margin="0,0,8,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnRefreshBlacklist" Appearance="Secondary"
                                   Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                   Click="OnRefreshBlacklistClick" Margin="0,0,0,4"
                                   Style="{StaticResource NeonButtonStyle}"/>
                        <TextBlock x:Name="TxtBlacklistCount" Text="—" FontSize="12"
                                   Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" Margin="12,0,0,4"/>
                    </WrapPanel>

                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridBlacklist"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserReorderColumns="False"
                                  IsReadOnly="True"
                                  SelectionMode="Single"
                                  GridLinesVisibility="None"
                                  HeadersVisibility="Column"
                                  BorderThickness="1"
                                  BorderBrush="{StaticResource NeonDangerBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="44"
                                  Style="{StaticResource NeonDataGridStyle}">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="{Binding NetColProgram, Source={StaticResource Loc}}"
                                                    Binding="{Binding ProcessName}" Width="2*"/>
                                <DataGridTextColumn Header="{Binding NetColFullPath, Source={StaticResource Loc}}"
                                                    Binding="{Binding FullPath}" Width="3*"/>
                                <DataGridTextColumn Header="{Binding NetColBlockedAt, Source={StaticResource Loc}}"
                                                    Binding="{Binding BlockedAtText}" Width="180"/>
                                <DataGridTextColumn Header="{Binding NetColReason, Source={StaticResource Loc}}"
                                                    Binding="{Binding Reason}" Width="200"/>
                                <DataGridTemplateColumn Header="{Binding NetColAction, Source={StaticResource Loc}}" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="{StaticResource NeonDangerGlow}"
                                                    BorderBrush="{StaticResource NeonDangerBrush}"
                                                    BorderThickness="1"
                                                    CornerRadius="{StaticResource BadgeCornerRadius}"
                                                    Padding="6,2"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="Заблокирован" FontSize="11" FontWeight="SemiBold" Foreground="White"/>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <StackPanel x:Name="PanelBlacklistEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="BlockContact24" FontSize="40"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,12"/>
                            <TextBlock Text="{Binding NetNoBlacklistTitle, Source={StaticResource Loc}}" FontSize="15" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       HorizontalAlignment="Center"/>
                            <TextBlock Text="{Binding NetNoBlacklistSubtitle, Source={StaticResource Loc}}" FontSize="13"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,6,0,0"/>
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ----------------------------------------------------------------
                 Вкладка 4: Правила брандмауэра (System Rules)
                 ---------------------------------------------------------------- -->
            <TabItem Header="{Binding NetTabFirewallRules, Source={StaticResource Loc}}">
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <Grid Grid.Row="0" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*"/>
                            <ColumnDefinition Width="3*"/>
                        </Grid.ColumnDefinitions>
                        <ui:TextBox x:Name="TxtRuleSearch" Grid.Column="0"
                                     PlaceholderText="{Binding NetSearchRulesHint, Source={StaticResource Loc}}"
                                     TextChanged="OnRuleSearchTextChanged" Margin="0,0,12,0"/>
                        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="{Binding NetRulesFilterLabel, Source={StaticResource Loc}}" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="0,0,8,0"/>
                            <RadioButton x:Name="FilterAll" GroupName="RuleFilter"
                                         Content="{Binding NetRulesFilterAll, Source={StaticResource Loc}}" IsChecked="True"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="FilterInbound" GroupName="RuleFilter"
                                         Content="{Binding NetRulesFilterInbound, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="FilterOutbound" GroupName="RuleFilter"
                                         Content="{Binding NetRulesFilterOutbound, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="FilterBlocking" GroupName="RuleFilter"
                                         Content="{Binding NetRulesFilterBlock, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0"/>
                            <RadioButton x:Name="FilterManaged" GroupName="RuleFilter"
                                         Content="StartupMonitor"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center"/>
                        </StackPanel>
                    </Grid>

                    <WrapPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:Button x:Name="BtnCreateRule" Appearance="Primary"
                                   Content="{Binding NetBtnCreateRule, Source={StaticResource Loc}}"
                                   Click="OnCreateRuleClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnEditRule" Appearance="Secondary"
                                   Content="{Binding NetBtnEditRule, Source={StaticResource Loc}}"
                                   Click="OnEditRuleClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnDeleteRule" Appearance="Secondary"
                                   Content="{Binding NetBtnDeleteRule, Source={StaticResource Loc}}"
                                   Click="OnDeleteRuleClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnExportRules" Appearance="Secondary"
                                   Icon="{ui:SymbolIcon Save24}"
                                   Content="{Binding NetBtnExport, Source={StaticResource Loc}}"
                                   ToolTip="{Binding NetBtnExportTip, Source={StaticResource Loc}}"
                                   Click="OnExportRulesClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnImportRules" Appearance="Secondary"
                                   Icon="{ui:SymbolIcon FolderOpen24}"
                                   Content="{Binding NetBtnImport, Source={StaticResource Loc}}"
                                   ToolTip="{Binding NetBtnImportTip, Source={StaticResource Loc}}"
                                   Click="OnImportRulesClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:DropDownButton x:Name="BtnSecurityPresets" Appearance="Secondary"
                                           Icon="{ui:SymbolIcon ShieldCheckmark24}"
                                           Content="{Binding NetBtnSecurityPresets, Source={StaticResource Loc}}"
                                           Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}">
                            <ui:DropDownButton.Flyout>
                                <ContextMenu>
                                    <MenuItem Header="{Binding NetPresetMaxPrivacy, Source={StaticResource Loc}}" Click="OnApplyPresetMaxPrivacyClick"/>
                                    <MenuItem Header="{Binding NetPresetGaming, Source={StaticResource Loc}}" Click="OnApplyPresetGamingClick"/>
                                    <MenuItem Header="{Binding NetPresetPublicWifi, Source={StaticResource Loc}}" Click="OnApplyPresetPublicWifiClick"/>
                                    <Separator/>
                                    <MenuItem Header="{Binding NetPresetReset, Source={StaticResource Loc}}" Click="OnApplyPresetResetClick"/>
                                </ContextMenu>
                            </ui:DropDownButton.Flyout>
                        </ui:DropDownButton>
                        <ui:Button x:Name="BtnRefreshRules" Appearance="Secondary"
                                   Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                   Click="OnRefreshRulesClick" Margin="0,0,8,4" Style="{StaticResource NeonButtonStyle}"/>
                        <ui:Button x:Name="BtnAntiTelemetry" Appearance="Danger"
                                   Content="{Binding NetBtnAntiTelemetry, Source={StaticResource Loc}}"
                                   Click="OnAntiTelemetryClick" Margin="0,0,0,4"
                                   ToolTip="{Binding NetBtnAntiTelemetryTip, Source={StaticResource Loc}}"
                                   Style="{StaticResource NeonButtonStyle}"/>
                    </WrapPanel>

                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridRules"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserReorderColumns="False"
                                  IsReadOnly="True"
                                  SelectionMode="Single"
                                  GridLinesVisibility="None"
                                  HeadersVisibility="Column"
                                  BorderThickness="1"
                                  BorderBrush="{StaticResource NeonAccentBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="38"
                                  Style="{StaticResource NeonDataGridStyle}"
                                  MouseDoubleClick="OnRuleGridDoubleClick">
                            <DataGrid.Columns>
                                <DataGridTemplateColumn Header="{Binding NetColEnabled, Source={StaticResource Loc}}" Width="52">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}"
                                                      Checked="OnRuleToggleChecked"
                                                      Unchecked="OnRuleToggleChecked"
                                                      HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="{Binding NetColAction, Source={StaticResource Loc}}" Width="110">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <ContentControl Content="{Binding Action}">
                                                <ContentControl.Style>
                                                    <Style TargetType="ContentControl">
                                                        <Setter Property="ContentTemplate" Value="{StaticResource AllowBadgeTemplate}"/>
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding Action}" Value="{x:Static local:FirewallAction.Block}">
                                                                <Setter Property="ContentTemplate" Value="{StaticResource BlockBadgeTemplate}"/>
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </ContentControl.Style>
                                            </ContentControl>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="{Binding NetColDirection, Source={StaticResource Loc}}" Width="80">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}"
                                                    CornerRadius="{StaticResource BadgeCornerRadius}" Padding="6,2"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock FontSize="11" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Text" Value="In"/>
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Direction}" Value="{x:Static local:FirewallDirection.Outbound}">
                                                                    <Setter Property="Text" Value="Out"/>
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding Direction}" Value="{x:Static local:FirewallDirection.Both}">
                                                                    <Setter Property="Text" Value="In+Out"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTextColumn Header="{Binding NetColName, Source={StaticResource Loc}}" Binding="{Binding Name}" Width="2*"/>
                                <DataGridTextColumn Header="{Binding NetColProgram, Source={StaticResource Loc}}" Binding="{Binding ShortProgram}" Width="1.5*"/>
                                <DataGridTextColumn Header="{Binding NetColProtocol, Source={StaticResource Loc}}" Binding="{Binding ProtocolDisplay}" Width="80"/>
                                <DataGridTextColumn Header="{Binding NetColLocalPorts, Source={StaticResource Loc}}" Binding="{Binding LocalPorts}" Width="110"/>
                                <DataGridTextColumn Header="{Binding NetColRemoteAddresses, Source={StaticResource Loc}}" Binding="{Binding RemoteAddresses}" Width="1.5*"/>
                                <DataGridTextColumn Header="{Binding NetColProfiles, Source={StaticResource Loc}}" Binding="{Binding ProfilesDisplay}" Width="130"/>
                            </DataGrid.Columns>
                        </DataGrid>

                        <StackPanel x:Name="PanelRulesEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="Shield24" FontSize="36"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,10"/>
                            <TextBlock Text="{Binding NetNoRulesTitle, Source={StaticResource Loc}}" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       HorizontalAlignment="Center"/>
                            <TextBlock Text="{Binding NetNoRulesSubtitle, Source={StaticResource Loc}}" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,4,0,0"/>
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ----------------------------------------------------------------
                 Вкладка 5: Port Defender
                 ---------------------------------------------------------------- -->
            <TabItem Header="{Binding NetTabPortDefender, Source={StaticResource Loc}}">
                <Grid Margin="0,8,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <ui:Card Grid.Row="0" Style="{StaticResource GlassCardStyle}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <Border Grid.Column="0" Width="46" Height="46" CornerRadius="{StaticResource BadgeCornerRadius}"
                                    Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                    Margin="0,0,14,0" VerticalAlignment="Center">
                                <ui:SymbolIcon Symbol="Shield24" FontSize="22"
                                               Foreground="{StaticResource NeonAccentBrush}"
                                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock x:Name="TxtDefenderState" Text="{Binding NetDefenderStateLoading, Source={StaticResource Loc}}"
                                           FontSize="13" FontWeight="SemiBold"
                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"/>
                                <TextBlock x:Name="TxtDefenderDetails" Text="—" FontSize="12"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" Margin="0,2,0,0"/>
                            </StackPanel>
                            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                <ui:Button x:Name="BtnConfigureDefender" Appearance="Primary"
                                           Content="{Binding PortDefenderTitle, Source={StaticResource Loc}}"
                                           Click="OnConfigureDefenderClick" Margin="0,0,8,0"
                                           Style="{StaticResource NeonButtonStyle}"/>
                                <ui:Button x:Name="BtnRefreshDefender" Appearance="Secondary"
                                           Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                           Click="OnRefreshDefenderClick" Style="{StaticResource NeonButtonStyle}"/>
                            </StackPanel>
                        </Grid>
                    </ui:Card>

                    <ui:Card Grid.Row="1" Style="{StaticResource GlassCardStyle}">
                        <StackPanel>
                            <TextBlock Text="{Binding PortDefenderProtectedPortsLabel, Source={StaticResource Loc}}"
                                       FontSize="12" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,0,0,8"/>
                            <ItemsControl x:Name="PortDefenderPortsList" Margin="0,0,0,8">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <WrapPanel Orientation="Horizontal"/>
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border Background="{StaticResource NeonAccentGlow}"
                                                BorderBrush="{StaticResource NeonAccentBrush}"
                                                BorderThickness="1"
                                                CornerRadius="{StaticResource BadgeCornerRadius}"
                                                Padding="8,4" Margin="0,0,8,8">
                                            <TextBlock Text="{Binding}" FontFamily="Consolas, Segoe UI"
                                                       FontSize="12" FontWeight="SemiBold"
                                                       Foreground="{StaticResource NeonAccentBrush}"/>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock x:Name="TxtDefenderPortsEmpty" Text="Нет защищаемых портов" FontSize="13"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       Visibility="Collapsed"/>
                        </StackPanel>
                    </ui:Card>
                </Grid>
            </TabItem>

        </TabControl>

        <!-- ===== Статусная строка ===== -->
        <Grid Grid.Row="3" Margin="0,12,0,0">
            <TextBlock x:Name="TxtStatus" Text="{Binding NetStatusReady, Source={StaticResource Loc}}" FontSize="12"
                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
        </Grid>
    </Grid>
</UserControl>
```

> **Примечание:** В XAML используется пространство имён `xmlns:local="clr-namespace:StartupMonitor.UI.Views"` для привязки enum-значений `FirewallAction` / `FirewallDirection` в `DataTrigger`. Конвертер `BrushConverter` — это стандартный `BrushConverter` из `System.Windows.Media`, зарегистрированный в ресурсах окна.

---

## 9. C# код-behind (NetworkSecurityHubView.xaml.cs) — новые и изменённые члены

Ниже приведены только новые и переопределённые члены. Остальной код (загрузка правил, банов, Port Defender, экспорт/импорт, пресеты) остаётся без изменений.

### 9.1 Поля и модели

```csharp
private readonly DispatcherTimer _whitelistRefreshTimer;
private readonly DispatcherTimer _blacklistRefreshTimer;
private bool _isLoadingWhitelist;
private bool _isLoadingBlacklist;
private List<FirewallWhitelistRow> _whitelistRows = new();
private List<FirewallBlacklistRow> _blacklistRows = new();
private string _whitelistSearchText = string.Empty;
private string _blacklistSearchText = string.Empty;

public sealed class FirewallWhitelistRow : INotifyPropertyChanged
{
    public string FullPath { get; set; } = string.Empty;
    public string ProcessName => System.IO.Path.GetFileName(FullPath);
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string AddedAtText => AddedAt.ToLocalTime().ToString("g");
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FirewallBlacklistRow : INotifyPropertyChanged
{
    public string FullPath { get; set; } = string.Empty;
    public string ProcessName => System.IO.Path.GetFileName(FullPath);
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    public string BlockedAtText => BlockedAt.ToLocalTime().ToString("g");
    public string Reason { get; set; } = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;
}
```

### 9.2 Инициализация таймеров (в конструкторе)

```csharp
_whitelistRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
_whitelistRefreshTimer.Tick += async (_, _) => await LoadWhitelistAsync(silent: true);

_blacklistRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
_blacklistRefreshTimer.Tick += async (_, _) => await LoadBlacklistAsync(silent: true);
```

### 9.3 Загрузка белого и чёрного списка

```csharp
private async Task LoadWhitelistAsync(bool silent)
{
    if (_isLoadingWhitelist) return;
    _isLoadingWhitelist = true;
    try
    {
        if (!silent) SetStatus("Загрузка белого списка приложений…");
        var paths = await _pipeClient.GetFirewallWhitelistAsync() ?? new List<string>();
        _whitelistRows = paths
            .Select(p => new FirewallWhitelistRow { FullPath = p, AddedAt = DateTime.UtcNow })
            .OrderBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyWhitelistFilter();
        TxtWhitelistCount.Text = $"В белом списке: {_whitelistRows.Count}";
        PanelWhitelistEmpty.Visibility = _whitelistRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!silent) SetStatus($"Загружено в белом списке: {_whitelistRows.Count}.");
    }
    catch (Exception ex)
    {
        SetStatus($"Не удалось загрузить белый список: {ex.Message}");
    }
    finally { _isLoadingWhitelist = false; }
}

private async Task LoadBlacklistAsync(bool silent)
{
    if (_isLoadingBlacklist) return;
    _isLoadingBlacklist = true;
    try
    {
        if (!silent) SetStatus("Загрузка чёрного списка приложений…");
        var paths = await _pipeClient.GetFirewallBlacklistAsync() ?? new List<string>();
        _blacklistRows = paths
            .Select(p => new FirewallBlacklistRow { FullPath = p, BlockedAt = DateTime.UtcNow, Reason = "Сетевая активность заблокирована" })
            .OrderBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyBlacklistFilter();
        TxtBlacklistCount.Text = $"В чёрном списке: {_blacklistRows.Count}";
        PanelBlacklistEmpty.Visibility = _blacklistRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!silent) SetStatus($"Загружено в чёрном списке: {_blacklistRows.Count}.");
    }
    catch (Exception ex)
    {
        SetStatus($"Не удалось загрузить чёрный список: {ex.Message}");
    }
    finally { _isLoadingBlacklist = false; }
}
```

### 9.4 Фильтрация и поиск

```csharp
private void ApplyWhitelistFilter()
{
    try
    {
        var q = (_whitelistSearchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _whitelistRows.Where(r =>
            string.IsNullOrEmpty(q) ||
            r.ProcessName.ToLowerInvariant().Contains(q) ||
            r.FullPath.ToLowerInvariant().Contains(q)).ToList();
        GridWhitelist.ItemsSource = filtered;
        PanelWhitelistEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    catch (Exception ex) { SetStatus($"Ошибка фильтрации белого списка: {ex.Message}"); }
}

private void OnWhitelistSearchChanged(object sender, TextChangedEventArgs e)
{
    try { _whitelistSearchText = TxtWhitelistSearch.Text ?? string.Empty; ApplyWhitelistFilter(); }
    catch (Exception ex) { SetStatus($"Ошибка поиска белого списка: {ex.Message}"); }
}

private void ApplyBlacklistFilter()
{
    try
    {
        var q = (_blacklistSearchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _blacklistRows.Where(r =>
            string.IsNullOrEmpty(q) ||
            r.ProcessName.ToLowerInvariant().Contains(q) ||
            r.FullPath.ToLowerInvariant().Contains(q)).ToList();
        GridBlacklist.ItemsSource = filtered;
        PanelBlacklistEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    catch (Exception ex) { SetStatus($"Ошибка фильтрации чёрного списка: {ex.Message}"); }
}

private void OnBlacklistSearchChanged(object sender, TextChangedEventArgs e)
{
    try { _blacklistSearchText = TxtBlacklistSearch.Text ?? string.Empty; ApplyBlacklistFilter(); }
    catch (Exception ex) { SetStatus($"Ошибка поиска чёрного списка: {ex.Message}"); }
}
```

### 9.5 Обработчики белого списка

```csharp
private async void OnAddWhitelistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Добавление программы в белый список брандмауэра"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = dlg.FileName;
        if (_whitelistRows.Any(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("Программа уже находится в белом списке.");
            return;
        }

        SetStatus($"Добавление «{System.IO.Path.GetFileName(path)}» в белый список…");
        var ok = await _pipeClient.AddToFirewallWhitelistAsync(path);
        if (ok)
        {
            _whitelistRows.Insert(0, new FirewallWhitelistRow { FullPath = path, AddedAt = DateTime.UtcNow });
            ApplyWhitelistFilter();
            SetStatus("Программа добавлена в белый список.");
        }
        else
        {
            SetStatus("Служба не подтвердила добавление в белый список.");
        }
    }
    catch (Exception ex) { SetStatus($"Ошибка добавления в белый список: {ex.Message}"); }
}

private async void OnRemoveWhitelistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var row = GridWhitelist.SelectedItem as FirewallWhitelistRow;
        if (row == null) { SetStatus("Выберите программу для удаления из белого списка."); return; }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"Удалить «{row.ProcessName}» из белого списка брандмауэра?",
            "Удаление из белого списка",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        SetStatus($"Удаление «{row.ProcessName}» из белого списка…");
        var ok = await _pipeClient.RemoveFromFirewallWhitelistAsync(row.FullPath);
        if (ok)
        {
            _whitelistRows.Remove(row);
            ApplyWhitelistFilter();
            SetStatus("Программа удалена из белого списка.");
        }
        else
        {
            SetStatus("Служба не подтвердила удаление из белого списка.");
        }
    }
    catch (Exception ex) { SetStatus($"Ошибка удаления из белого списка: {ex.Message}"); }
}

private async void OnRefreshWhitelistClick(object sender, RoutedEventArgs e)
{
    try { await LoadWhitelistAsync(silent: false); }
    catch (Exception ex) { SetStatus($"Ошибка обновления белого списка: {ex.Message}"); }
}

private async void OnExportWhitelistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            FileName = $"FirewallWhitelist_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var json = System.Text.Json.JsonSerializer.Serialize(_whitelistRows.Select(r => r.FullPath).ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(dlg.FileName, json);
        SetStatus($"Белый список экспортирован: {_whitelistRows.Count} записей.");
    }
    catch (Exception ex) { SetStatus($"Ошибка экспорта белого списка: {ex.Message}"); }
}
```

### 9.6 Обработчики чёрного списка

```csharp
private async void OnAddBlacklistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Добавление программы в чёрный список брандмауэра"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = dlg.FileName;
        if (_blacklistRows.Any(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("Программа уже находится в чёрном списке.");
            return;
        }

        SetStatus($"Добавление «{System.IO.Path.GetFileName(path)}» в чёрный список…");
        var ok = await _pipeClient.AddToFirewallBlacklistAsync(path, "Добавлено пользователем из интерфейса Firewall");
        if (ok)
        {
            _blacklistRows.Insert(0, new FirewallBlacklistRow
            {
                FullPath = path,
                BlockedAt = DateTime.UtcNow,
                Reason = "Добавлено пользователем"
            });
            ApplyBlacklistFilter();
            SetStatus("Программа добавлена в чёрный список.");
        }
        else
        {
            SetStatus("Служба не подтвердила добавление в чёрный список.");
        }
    }
    catch (Exception ex) { SetStatus($"Ошибка добавления в чёрный список: {ex.Message}"); }
}

private async void OnRemoveBlacklistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var row = GridBlacklist.SelectedItem as FirewallBlacklistRow;
        if (row == null) { SetStatus("Выберите программу для удаления из чёрного списка."); return; }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"Убрать «{row.ProcessName}» из чёрного списка (снять блокирующее правило)?",
            "Удаление из чёрного списка",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        SetStatus($"Удаление «{row.ProcessName}» из чёрного списка…");
        var ok = await _pipeClient.RemoveFromFirewallBlacklistAsync(row.FullPath);
        if (ok)
        {
            _blacklistRows.Remove(row);
            ApplyBlacklistFilter();
            SetStatus("Программа удалена из чёрного списка (блокировка снята).");
        }
        else
        {
            SetStatus("Служба не подтвердила удаление из чёрного списка.");
        }
    }
    catch (Exception ex) { SetStatus($"Ошибка удаления из чёрного списка: {ex.Message}"); }
}

private async void OnRefreshBlacklistClick(object sender, RoutedEventArgs e)
{
    try { await LoadBlacklistAsync(silent: false); }
    catch (Exception ex) { SetStatus($"Ошибка обновления чёрного списка: {ex.Message}"); }
}

private async void OnExportBlacklistClick(object sender, RoutedEventArgs e)
{
    try
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            FileName = $"FirewallBlacklist_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var json = System.Text.Json.JsonSerializer.Serialize(_blacklistRows.Select(r => new { r.FullPath, r.BlockedAt, r.Reason }).ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(dlg.FileName, json);
        SetStatus($"Чёрный список экспортирован: {_blacklistRows.Count} записей.");
    }
    catch (Exception ex) { SetStatus($"Ошибка экспорта чёрного списка: {ex.Message}"); }
}
```

### 9.7 Общий RefreshAllAsync — добавление загрузки списков

В метод `RefreshAllAsync()` добавить вызовы:
```csharp
await LoadWhitelistAsync(silent: true);
await LoadBlacklistAsync(silent: true);
```

И в `Deactivate()` добавить остановку `_whitelistRefreshTimer` и `_blacklistRefreshTimer`; в `Activate()` — их запуск.

### 9.8 Обработчик переключения вкладок

```csharp
private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    try
    {
        var selected = MainTabs.SelectedItem as TabItem;
        if (selected == null) return;

        // Активируем таймеры только для вкладок, которым нужно фоновое обновление
        if (selected == (TabItem)MainTabs.Items[1]) // Whitelist
        {
            if (!_whitelistRefreshTimer.IsEnabled) _whitelistRefreshTimer.Start();
        }
        else if (selected == (TabItem)MainTabs.Items[2]) // Blacklist
        {
            if (!_blacklistRefreshTimer.IsEnabled) _blacklistRefreshTimer.Start();
        }
    }
    catch { }
}
```

---

## 10. Изменения в PipeServer.cs (сторона службы)

Добавить обработчики в `switch (message.Type)`:

```csharp
case PipeMessageTypes.GetFirewallWhitelist:
{
    var settings = _store.GetSettings();
    response.Type = PipeMessageTypes.GetFirewallWhitelistResponse;
    response.Payload = JsonSerializer.Serialize(settings.FirewallWhitelistedApps, JsonOptions);
    break;
}
case PipeMessageTypes.AddToFirewallWhitelist:
{
    var payload = JsonSerializer.Deserialize<FirewallAppActionPayload>(message.Payload, JsonOptions);
    if (payload != null && !string.IsNullOrWhiteSpace(payload.Path))
    {
        var settings = _store.GetSettings();
        if (!settings.FirewallWhitelistedApps.Contains(payload.Path, StringComparer.OrdinalIgnoreCase))
        {
            settings.FirewallWhitelistedApps.Add(payload.Path);
            _store.Save(_store.Load()); // или SaveSettings напрямую
        }
    }
    response.Type = PipeMessageTypes.Success;
    break;
}
case PipeMessageTypes.RemoveFromFirewallWhitelist:
{
    var payload = JsonSerializer.Deserialize<FirewallAppActionPayload>(message.Payload, JsonOptions);
    if (payload != null)
    {
        var settings = _store.GetSettings();
        settings.FirewallWhitelistedApps.RemoveAll(p => string.Equals(p, payload.Path, StringComparison.OrdinalIgnoreCase));
        _store.Save(_store.Load());
    }
    response.Type = PipeMessageTypes.Success;
    break;
}
// Аналогично для GetFirewallBlacklist / AddToFirewallBlacklist / RemoveFromFirewallBlacklist
// с полем FirewallBlacklistedApps.
```

---

## 11. Локализация (новые ключи)

Добавить в ресурсы локализации (`LocalizationService` / `.resx`):

| Ключ | RU | EN |
|------|----|----|
| `NetTabWhitelist` | Белый список | Whitelist |
| `NetTabBlacklist` | Чёрный список | Blacklist |
| `NetSearchWhitelistHint` | Поиск в белом списке… | Search whitelist… |
| `NetSearchBlacklistHint` | Поиск в чёрном списке… | Search blacklist… |
| `NetBtnAddToWhitelist` | Добавить программу | Add program |
| `NetBtnRemoveFromWhitelist` | Удалить из списка | Remove from list |
| `NetBtnAddToBlacklist` | Добавить программу | Add program |
| `NetBtnRemoveFromBlacklist` | Убрать из списка | Remove from list |
| `NetColFullPath` | Полный путь | Full path |
| `NetColAddedAt` | Добавлено | Added |
| `NetColBlockedAt` | Заблокировано | Blocked |
| `NetColReason` | Причина | Reason |
| `NetNoWhitelistTitle` | Белый список пуст | Whitelist is empty |
| `NetNoWhitelistSubtitle` | Добавьте программы, которым разрешён сетевой доступ | Add programs allowed network access |
| `NetNoBlacklistTitle` | Чёрный список пуст | Blacklist is empty |
| `NetNoBlacklistSubtitle` | Заблокированные программы будут отображены здесь | Blocked programs will appear here |

---

## 12. План интеграции и миграции

1. **Добавить модели** `FirewallWhitelistRow`, `FirewallBlacklistRow` в `NetworkSecurityHubView.xaml.cs` (или вынести в `StartupMonitor.UI.Models`).
2. **Добавить типы сообщений** в `PipeMessageTypes` и полезные нагрузки `FirewallAppActionPayload`.
3. **Добавить методы** в `PipeClient`: `GetFirewallWhitelistAsync`, `AddToFirewallWhitelistAsync`, `RemoveFromFirewallWhitelistAsync`, `GetFirewallBlacklistAsync`, `AddToFirewallBlacklistAsync`, `RemoveFromFirewallBlacklistAsync`.
4. **Добавить обработчики** в `PipeServer` для синхронизации с `AppSettings.FirewallWhitelistedApps` / `FirewallBlacklistedApps`.
5. **Заменить XAML** `NetworkSecurityHubView.xaml` на новую разметку с 5 вкладками и неоновыми стилями.
6. **Обновить C# код-behind**: новые обработчики, таймеры, фильтрация, загрузка списков в `RefreshAllAsync`.
7. **Добавить ключи локализации** для новых вкладок и действий.
8. **Проверить компиляцию** и запустить UI-тесты (`FirewallHubUiTests`, `FirewallUiIntegrationTests`).

---

## 13. Критерий приёмки

- [ ] 5 вкладок отображаются с неоновым стилем (светящиеся акценты, `CornerRadius="8"`, тени, полупрозрачность)
- [ ] Вкладка «Белый список» показывает таблицу с путём, именем, датой добавления
- [ ] Кнопки «Добавить программу» / «Удалить из списка» работают через Named Pipe
- [ ] Вкладка «Чёрный список» показывает таблицу с путём, именем, датой блокировки, причиной
- [ ] Кнопка «Убрать из списка» снимает блокирующее правило через Named Pipe
- [ ] Данные синхронизируются с `AppSettings.FirewallWhitelistedApps` / `FirewallBlacklistedApps`
- [ ] Старая функциональность (соединения, правила, Port Defender, баны) сохранена без регрессов
- [ ] Zero-Crash: все новые вызовы обёрнуты в try-catch, ошибки в статусную строку

---

## 14. Оценка трудозатрат

| Этап | Часы |
|------|------|
| Аудит и проектирование | 4 |
| Новые PipeClient / PipeServer методы | 6 |
| XAML разметка 5 вкладок | 10 |
| C# обработчики (whitelist/blacklist) | 8 |
| Локализация и ресурсы | 2 |
| Тестирование и отладка | 6 |
| **Итого** | **36** |

---

*Отчет подготовлен ведущим WPF/XAL архитектором проекта StartupMonitor. Макеты кода готовы к переносу в исходные файлы `NetworkSecurityHubView.xaml` и `NetworkSecurityHubView.xaml.cs`.*
