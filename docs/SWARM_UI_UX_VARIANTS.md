Проектирую 4 варианта сетевой защиты — изучаю текущие окна для интеграции.
Нашёл базу — теперь готовлю четыре детальных дизайн-концепта с XAML и сценариями.
# Дизайн сетевой безопасности StartupMonitor — 4 варианта

> Стек: `Wpf.Ui 3.x (FluentWindow, Card, SymbolIcon)`, `Windows 11 Mica`, `DynamicResource` + `ui:ThemeResource`. Все варианты совместимы с существующими `ProcessConnectionsWindow.xaml` и `MainWindow.xaml`, бэкенд уже есть: `WindowsFirewallService.BlockIp / BlockPort / IsIpBlocked / ListManagedRules`.

## Общие дизайн-токены (положить в `App.xaml` / `Themes`)

```xml
<!-- Палитра статусов — одинакова для всех 4 вариантов -->
<SolidColorBrush x:Key="ShieldAllowedBg" Color="#20107C41"/>
<SolidColorBrush x:Key="ShieldAllowedFg" Color="#10B981"/>
<SolidColorBrush x:Key="ShieldBlockedBg" Color="#26EF4444"/>
<SolidColorBrush x:Key="ShieldBlockedFg" Color="#EF4444"/>
<SolidColorBrush x:Key="ShieldProtectedBg" Color="#1A3B82F6"/>
<SolidColorBrush x:Key="ShieldProtectedFg" Color="#60A5FA"/>
<SolidColorBrush x:Key="ShieldWarnBg" Color="#26F59E0B"/>
<SolidColorBrush x:Key="ShieldWarnFg" Color="#F59E0B"/>

<!-- Dark / Light переключаются автоматически через ui:ThemeResource.
     Ниже — только семантические кисти, уже theme-aware: -->
<!-- TextFillColorPrimaryBrush, CardBackgroundFillColorDefaultBrush,
     CardStrokeColorDefaultBrush, AccentTextFillColorPrimaryBrush — из Wpf.Ui -->
```

| Статус | Dark | Light | Смысл |
|---|---|---|---|
| 🟢 Разрешено | `#20107C41` на `#1E1E1E` | `#E6F4EA` на `#FFFFFF` | Трафик идёт |
| 🔴 Заблокирован | `#26EF4444` | `#FDECEC` | Правило Block в Firewall |
| 🔵 Под защитой | `#1A3B82F6` | `#E8F0FE` | Port Defender мониторит порт |
| 🟡 Временный бан | `#26F59E0B` | `#FFF7E6` | Авто-бан с таймером |

---

## ВАРИАНТ 1. Контекстный инспектор в `ProcessConnectionsWindow`

**Идея:** минимальное вторжение — добавляем сводку + колонку + контекстное меню. Пользователь видит угрозу там, где уже смотрит сокеты.

### 1A. Верхняя Shield-карточка (вставить после `ui:Card` процесса, `Grid.Row=1`)

```xml
<!-- Defender Summary Card: Grid.Row=1, существующую карточку сдвинуть -->
<ui:Card Grid.Row="1" Margin="0,0,0,12" Padding="12"
         Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="Auto"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>

    <!-- Щит статуса -->
    <Border Grid.Column="0" Width="40" Height="40" CornerRadius="20"
            Margin="0,0,12,0" VerticalAlignment="Center"
            Background="{Binding DefenderBannerBg}">
      <ui:SymbolIcon Symbol="{Binding DefenderBannerIcon}"
                     FontSize="20"
                     Foreground="{Binding DefenderBannerFg}"
                     HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Border>

    <StackPanel Grid.Column="1" VerticalAlignment="Center">
      <StackPanel Orientation="Horizontal">
        <TextBlock Text="{Binding DefenderTitle}" FontSize="14" FontWeight="SemiBold"
                   Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"/>
        <Border CornerRadius="4" Padding="6,2" Margin="8,0,0,0" VerticalAlignment="Center"
                Background="{Binding DefenderBannerBg}">
          <TextBlock Text="{Binding DefenderStateText}" FontSize="11" FontWeight="SemiBold"
                     Foreground="{Binding DefenderBannerFg}"/>
        </Border>
      </StackPanel>
      <TextBlock Text="{Binding DefenderSubtitle}" FontSize="12" Margin="0,2,0,0"
                 Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                 TextTrimming="CharacterEllipsis"/>
      <!-- Пример: "Мониторинг: 3 порта • Банов: 2 • Белый список: 5" -->
    </StackPanel>

    <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
      <ui:Button Content="Настроить Defender" Appearance="Secondary"
                 Icon="{ui:SymbolIcon Shield24}"
                 Click="OnConfigureDefenderClick" Margin="0,0,8,0"/>
      <ui:ToggleSwitch x:Name="TglDefender" Content="Defender"
                       IsChecked="{Binding IsPortDefenderEnabled, Mode=TwoWay}"
                       Toggled="OnDefenderToggled"
                       ToolTip="Включить/выключить Port Defender для этого процесса"/>
    </StackPanel>
  </Grid>
</ui:Card>
```

Состояния баннера (`DefenderBannerBg/Fg/Icon/Title`):

| `IsPortDefenderEnabled` | `ActiveBans>0` | Иконка | Заголовок |
|---|---|---|---|
| `false` | — | `ShieldDismiss24`, серый | «Port Defender выключен» + кнопка «Включить Defender» (Appearance=`Primary`) |
| `true` | `0` | `ShieldCheckmark24`, зелёный | «Под защитой — атак не обнаружено» |
| `true` | `>0` | `ShieldError24`, красный + пульсация | «Отбито N атак • забанено M IP» |

### 1B. Новое контекстное меню + колонка Firewall

Заменить существующий `DataGrid.ContextMenu`:

```xml
<DataGrid.ContextMenu>
  <ContextMenu>
    <MenuItem Header="⛔ Заблокировать IP" Icon="{ui:SymbolIcon Stop24}"
              Click="OnBlockIpClick" FontWeight="SemiBold"
              ToolTip="Создать Inbound+Outbound Block-правило в Windows Firewall"/>
    <MenuItem Header="⛔ Заблокировать порт" Icon="{ui:SymbolIcon PlugDisconnected24}"
              Click="OnBlockPortClick"/>
    <MenuItem Header="🛡️ Настроить Port Defender…" Icon="{ui:SymbolIcon Shield24}"
              Click="OnConfigureDefenderClick"/>
    <Separator/>
    <MenuItem Header="✅ Разблокировать" Click="OnUnblockClick"
              Visibility="{Binding PlacementTarget.SelectedItem.FirewallStatus,
                RelativeSource={RelativeSource AncestorType=ContextMenu},
                Converter={StaticResource BlockedToVisibilityConverter}}"/>
    <Separator/>
    <MenuItem Header="Копировать удалённый IP" Click="OnCopyRemoteIpClick"/>
    <MenuItem Header="Копировать адрес:порт" Click="OnCopyRemoteEndpointClick"/>
    <Separator/>
    <MenuItem Header="Открыть Whois (IPInfo.io)" Click="OnOpenWhoisClick"/>
    <MenuItem Header="Проверить в VirusTotal" Click="OnOpenVirusTotalClick"/>
  </ContextMenu>
</DataGrid.ContextMenu>
```

Новая колонка после «Сегмент»:

```xml
<!-- Статус Firewall -->
<DataGridTemplateColumn Header="Firewall" Width="130">
  <DataGridTemplateColumn.CellTemplate>
    <DataTemplate>
      <Border CornerRadius="10" Padding="8,3" HorizontalAlignment="Left"
              VerticalAlignment="Center" BorderThickness="1">
        <Border.Style>
          <Style TargetType="Border">
            <Setter Property="Background" Value="#20107C41"/>
            <Setter Property="BorderBrush" Value="#40107C41"/>
            <Style.Triggers>
              <DataTrigger Binding="{Binding FirewallStatus}" Value="Blocked">
                <Setter Property="Background" Value="#26EF4444"/>
                <Setter Property="BorderBrush" Value="#80EF4444"/>
              </DataTrigger>
              <DataTrigger Binding="{Binding FirewallStatus}" Value="Protected">
                <Setter Property="Background" Value="#1A3B82F6"/>
                <Setter Property="BorderBrush" Value="#603B82F6"/>
              </DataTrigger>
              <DataTrigger Binding="{Binding FirewallStatus}" Value="TempBanned">
                <Setter Property="Background" Value="#26F59E0B"/>
                <Setter Property="BorderBrush" Value="#80F59E0B"/>
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Border.Style>
        <StackPanel Orientation="Horizontal">
          <ui:SymbolIcon FontSize="12" Margin="0,0,4,0" VerticalAlignment="Center">
            <ui:SymbolIcon.Style>
              <Style TargetType="ui:SymbolIcon">
                <Setter Property="Symbol" Value="CheckmarkCircle24"/>
                <Setter Property="Foreground" Value="#10B981"/>
                <Style.Triggers>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="Blocked">
                    <Setter Property="Symbol" Value="DismissCircle24"/>
                    <Setter Property="Foreground" Value="#EF4444"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="Protected">
                    <Setter Property="Symbol" Value="Shield24"/>
                    <Setter Property="Foreground" Value="#60A5FA"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="TempBanned">
                    <Setter Property="Symbol" Value="Timer24"/>
                    <Setter Property="Foreground" Value="#F59E0B"/>
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </ui:SymbolIcon.Style>
          </ui:SymbolIcon>
          <TextBlock FontSize="11" FontWeight="SemiBold">
            <TextBlock.Style>
              <Style TargetType="TextBlock">
                <Setter Property="Text" Value="Разрешено"/>
                <Setter Property="Foreground" Value="#10B981"/>
                <Style.Triggers>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="Blocked">
                    <Setter Property="Text" Value="Заблокирован"/>
                    <Setter Property="Foreground" Value="#EF4444"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="Protected">
                    <Setter Property="Text" Value="Под защитой"/>
                    <Setter Property="Foreground" Value="#60A5FA"/>
                  </DataTrigger>
                  <DataTrigger Binding="{Binding FirewallStatus}" Value="TempBanned">
                    <Setter Property="Text" Value="{Binding BanRemainingText}"/>
                    <Setter Property="Foreground" Value="#F59E0B"/>
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </TextBlock.Style>
          </TextBlock>
        </StackPanel>
      </Border>
    </DataTemplate>
  </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

Модель строки — расширить `ProcessNetworkSocket` обёрткой:

```csharp
// ProcessConnectionsWindow.xaml.cs — добавить
public enum SocketFirewallStatus { Allowed, Blocked, Protected, TempBanned }
public sealed class SocketRow : ProcessNetworkSocket {
  public SocketFirewallStatus FirewallStatus { get; set; }
  public string BanRemainingText { get; set; } = ""; // "Бан 14:32"
}
// В LoadConnectionsAsync: после GetProcessNetworkConnectionsAsync
// обогатить через _pipeClient.IsIpBlockedAsync(ip) + IsPortBlockedAsync(port)
```

### User Journey Bарианта 1
1. Пользователь открывает «Сетевые соединения» из Диспетчера задач → видит красный баннер «Отбито 3 атаки».
2. В таблице строка `185.220.x.x:443 → ESTABLISHED` подсвечена бейджем `[Под защитой]`.
3. Правый клик → «Заблокировать IP» → `ContentDialog` подтверждения («Создать правило Block In+Out для 185.220.x.x?») → Toast «IP заблокирован» + бейдж строки мгновенно становится `[Заблокирован]`.
4. Тумблер «Defender» в карточке — one-click включение без ухода из окна.

**Плюсы:** ноль новых окон, контекстная блокировка за 2 клика. **Минусы:** нет глобального обзора.

---

## ВАРИАНТ 2. Dedicated Network Security Hub (вкладка в `MainWindow`)

**Идея:** новый `NavItem8` «Сетевой экран» между «Чёрным списком» и «Удалёнными». Полноценный дашборд.

Навигация (`MainWindow.xaml`, после `NavItem1`):

```xml
<Border x:Name="NavItem8" Tag="8" MouseLeftButtonDown="OnNavClick"
        Style="{StaticResource NavItemStyle}">
  <Grid>
    <Rectangle x:Name="NavBar8" Width="3.5" Height="22" HorizontalAlignment="Left"
               RadiusX="1.75" RadiusY="1.75"
               Fill="{StaticResource NavActiveMarkerBrush}" Visibility="Collapsed"/>
    <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
      <Viewbox Width="16" Height="16">
        <Path Data="{StaticResource IconShieldCheckmark}"
              Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform"/>
      </Viewbox>
      <TextBlock Text="Сетевой экран" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0"/>
      <!-- Красная точка при активных атаках -->
      <Ellipse Width="8" Height="8" Fill="#EF4444" Margin="8,0,0,0"
               VerticalAlignment="Center"
               Visibility="{Binding HasActiveAttacks,
                 Converter={StaticResource BooleanToVisibilityConverter}}">
        <Ellipse.Effect>
          <DropShadowEffect Color="#EF4444" BlurRadius="8" Opacity="0.9" ShadowDepth="0"/>
        </Ellipse.Effect>
      </Ellipse>
    </StackPanel>
  </Grid>
</Border>
```

Контент страницы (`Grid x:Name="FirewallPage"`, аналог `OverviewPage`):

```xml
<Grid Grid.Column="1" x:Name="FirewallPage" Visibility="Collapsed">
  <ScrollViewer Padding="32,0,32,16" VerticalScrollBarVisibility="Auto">
    <StackPanel>
      <TextBlock Text="Сетевой экран" FontSize="28" FontWeight="700"
                 Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" Margin="0,0,0,4"/>
      <TextBlock Text="Брандмауэр Windows + Port Defender • живой мониторинг"
                 FontSize="13" Foreground="#94A3B8" Margin="0,0,0,16"/>

      <!-- Ряд 1: 4 KPI-карточки -->
      <Grid Margin="0,0,0,12">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/><ColumnDefinition Width="*"/>
          <ColumnDefinition Width="*"/><ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <!-- Defender master-switch -->
        <Border Grid.Column="0" Margin="0,0,6,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <StackPanel Orientation="Horizontal">
              <ui:SymbolIcon Symbol="Shield24" Foreground="#60A5FA" Margin="0,0,8,0"/>
              <TextBlock Text="Port Defender" FontSize="12" Foreground="#94A3B8"/>
            </StackPanel>
            <ui:ToggleSwitch IsChecked="{Binding IsPortDefenderEnabled, Mode=TwoWay}"
                             Content="{Binding DefenderStateLabel}" Margin="0,8,0,0"/>
            <TextBlock Text="{Binding DefenderThresholdSummary}" FontSize="11"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
          </StackPanel>
        </Border>
        <!-- Входящие / минуту + мини-спарклайн -->
        <Border Grid.Column="1" Margin="6,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <TextBlock Text="Входящих / мин" FontSize="12" Foreground="#94A3B8"/>
            <TextBlock Text="{Binding InboundPerMinute}" FontSize="32" FontWeight="700"/>
            <ProgressBar Value="{Binding InboundLoadPercent}" Maximum="100" Height="4" Margin="0,6,0,0"/>
          </StackPanel>
        </Border>
        <!-- Активные баны -->
        <Border Grid.Column="2" Margin="6,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <TextBlock Text="Активные баны" FontSize="12" Foreground="#94A3B8"/>
            <TextBlock Text="{Binding ActiveBansCount}" FontSize="32" FontWeight="700"
                       Foreground="#EF4444"/>
            <ui:Button Content="Снять все" Appearance="Transparent" FontSize="11"
                       Command="{Binding UnbanAllCommand}" HorizontalAlignment="Left"/>
          </StackPanel>
        </Border>
        <!-- Правил всего -->
        <Border Grid.Column="3" Margin="6,0,0,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <TextBlock Text="Управляемых правил" FontSize="12" Foreground="#94A3B8"/>
            <TextBlock Text="{Binding ManagedRulesCount}" FontSize="32" FontWeight="700"/>
            <TextBlock Text="{Binding FirewallServiceState}" FontSize="11"
                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"/>
          </StackPanel>
        </Border>
      </Grid>

      <!-- Ряд 2: Временные баны + Белый список -->
      <Grid Margin="0,0,0,12">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="1.4*"/><ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <Border Grid.Column="0" Margin="0,0,6,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
              <TextBlock Text="Временные баны" FontSize="14" FontWeight="600"/>
              <Border Style="{StaticResource BadgeRedPill}" Margin="8,0,0,0">
                <TextBlock Text="{Binding ActiveBansCount}" FontSize="10.5" FontWeight="700"
                           Foreground="{StaticResource BadgeRedTextBrush}"/>
              </Border>
            </StackPanel>
            <ListView ItemsSource="{Binding TempBans}" MaxHeight="220"
                      BorderThickness="0" Background="Transparent">
              <ListView.ItemTemplate>
                <DataTemplate>
                  <Grid Margin="0,4">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Grid.Column="0">
                      <TextBlock Text="{Binding Ip}" FontFamily="Consolas" FontWeight="600"/>
                      <TextBlock Text="{Binding Reason}" FontSize="11" Foreground="#94A3B8"/>
                    </StackPanel>
                    <!-- Обратный отсчёт -->
                    <Border Grid.Column="1" CornerRadius="6" Padding="8,4" Margin="8,0"
                            Background="#26F59E0B">
                      <TextBlock Text="{Binding RemainingText}" FontSize="11"
                                 FontWeight="600" Foreground="#F59E0B"/>
                    </Border>
                    <ui:Button Grid.Column="2" Content="Разбанить" Appearance="Secondary"
                               Command="{Binding DataContext.UnbanCommand,
                                 RelativeSource={RelativeSource AncestorType=ListView}}"
                               CommandParameter="{Binding}"/>
                  </Grid>
                </DataTemplate>
              </ListView.ItemTemplate>
            </ListView>
          </StackPanel>
        </Border>

        <Border Grid.Column="1" Margin="6,0,0,0" Style="{StaticResource FluentGlassCard}">
          <StackPanel>
            <TextBlock Text="Белый список" FontSize="14" FontWeight="600" Margin="0,0,0,8"/>
            <ui:TextBox PlaceholderText="Добавить IP…" Text="{Binding NewWhitelistIp, Mode=TwoWay}"/>
            <ui:Button Content="Добавить" Appearance="Primary" Margin="0,8,0,8"
                       Command="{Binding AddWhitelistCommand}"/>
            <ListBox ItemsSource="{Binding Whitelist}" MaxHeight="140"
                     BorderThickness="0" Background="Transparent"/>
          </StackPanel>
        </Border>
      </Grid>

      <!-- Ряд 3: Переключатели правил -->
      <Border Style="{StaticResource FluentGlassCard}">
        <StackPanel>
          <TextBlock Text="Правила защиты" FontSize="14" FontWeight="600" Margin="0,0,0,8"/>
          <StackPanel Orientation="Horizontal">
            <ui:ToggleSwitch Content="Блокировать сканеры портов" Margin="0,0,24,0"
                             IsChecked="{Binding BlockPortScanners}"/>
            <ui:ToggleSwitch Content="Бан за N попыток" Margin="0,0,24,0"
                             IsChecked="{Binding BanOnThreshold}"/>
            <ui:Button Content="Мастер настройки…" Appearance="Secondary"
                       Command="{Binding OpenDefenderWizardCommand}"
                       Icon="{ui:SymbolIcon Wizard24}"/>
          </StackPanel>
        </StackPanel>
      </Border>
    </StackPanel>
  </ScrollViewer>
</Grid>
```

ViewModel-контракт:

```csharp
bool IsPortDefenderEnabled { get; set; }
int InboundPerMinute { get; } double InboundLoadPercent { get; }
int ActiveBansCount { get; } ObservableCollection<TempBanDto> TempBans { get; }
ObservableCollection<string> Whitelist { get; }
ICommand UnbanCommand, UnbanAllCommand, AddWhitelistCommand, OpenDefenderWizardCommand;
```

### User Journey Варианта 2
1. Красная точка на пункте «Сетевой экран» привлекает внимание.
2. Пользователь видит: «Активные баны: 3», список с таймерами `13:42`, `59:01`, причину «SYN-flood :3389».
3. Кнопка «Разбанить» на строке / «Снять все» — отмена ложного срабатывания за 1 клик.
4. Добавляет домашний IP в белый список → тот никогда не банится.

---

## ВАРИАНТ 3. Interactive Flyout / Modal Wizard

**Идея:** `ContentDialog` в 3 шага. Открывается из Вариантов 1 и 2.

```xml
<!-- PortDefenderWizard.xaml — ui:ContentDialog -->
<ui:ContentDialog x:Class="StartupMonitor.UI.Views.Dialogs.PortDefenderWizard"
                  Title="Настройка Port Defender"
                  PrimaryButtonText="Включить защиту"
                  CloseButtonText="Отмена"
                  DefaultButton="Primary">
  <StackPanel Width="420">
    <!-- Шаги -->
    <StackPanel Orientation="Horizontal" Margin="0,0,0,12" HorizontalAlignment="Center">
      <Ellipse Width="24" Height="24" Fill="{Binding Step1Brush}"/>
      <Border Width="32" Height="1" Background="#64748B" VerticalAlignment="Center"/>
      <Ellipse Width="24" Height="24" Fill="{Binding Step2Brush}"/>
      <Border Width="32" Height="1" Background="#64748B" VerticalAlignment="Center"/>
      <Ellipse Width="24" Height="24" Fill="{Binding Step3Brush}"/>
    </StackPanel>

    <!-- Шаг 1: Порог -->
    <StackPanel Visibility="{Binding IsStep1, Converter={StaticResource BooleanToVisibilityConverter}}">
      <TextBlock Text="Шаг 1 — Порог срабатывания" FontWeight="600" Margin="0,0,0,8"/>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/><ColumnDefinition Width="70"/>
        </Grid.ColumnDefinitions>
        <Slider Grid.Column="0" Minimum="5" Maximum="500" Value="{Binding Threshold, Mode=TwoWay}"
                TickFrequency="5" IsSnapToTickEnabled="False" VerticalAlignment="Center"/>
        <ui:NumberBox Grid.Column="1" Value="{Binding Threshold, Mode=TwoWay}"
                      Minimum="5" Maximum="500" Margin="8,0,0,0"/>
      </Grid>
      <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
        <TextBlock Text="за" VerticalAlignment="Center" Margin="0,0,8,0"/>
        <ComboBox SelectedItem="{Binding ThresholdUnit}" Width="130">
          <ComboBoxItem Content="секунду"/><ComboBoxItem Content="минуту"/><ComboBoxItem Content="час"/>
        </ComboBox>
      </StackPanel>
      <!-- Живой пример -->
      <Border CornerRadius="8" Padding="10" Margin="0,12,0,0" Background="#1A3B82F6"
              BorderBrush="#603B82F6" BorderThickness="1">
        <TextBlock Text="{Binding ThresholdPreview}" FontSize="12" Foreground="#60A5FA"
                   TextWrapping="Wrap"/>
        <!-- «50 попыток/мин на порт 3389 → временный бан» -->
      </Border>
      <!-- Пресеты -->
      <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
        <ui:Button Content="Мягко (100/мин)" Appearance="Secondary" Margin="0,0,6,0"
                   Command="{Binding PresetGentleCommand}"/>
        <ui:Button Content="Сбалансировано (50/мин)" Appearance="Secondary" Margin="0,0,6,0"
                   Command="{Binding PresetBalancedCommand}"/>
        <ui:Button Content="Строго (10/мин)" Appearance="Secondary"
                   Command="{Binding PresetStrictCommand}"/>
      </StackPanel>
    </StackPanel>

    <!-- Шаг 2: Длительность бана -->
    <StackPanel Visibility="{Binding IsStep2, Converter={StaticResource BooleanToVisibilityConverter}}">
      <TextBlock Text="Шаг 2 — Длительность бана" FontWeight="600" Margin="0,0,0,8"/>
      <RadioButton Content="15 минут — для случайных сканеров" GroupName="Ban"
                   IsChecked="{Binding Ban15Min}" Margin="0,4"/>
      <RadioButton Content="1 час — рекомендуемое" GroupName="Ban"
                   IsChecked="{Binding Ban1Hour}" Margin="0,4"/>
      <RadioButton Content="24 часа — для упорных атак" GroupName="Ban"
                   IsChecked="{Binding Ban24Hours}" Margin="0,4"/>
      <RadioButton Content="Навсегда — создать постоянное правило Firewall"
                   GroupName="Ban" IsChecked="{Binding BanPermanent}" Margin="0,4"/>
    </StackPanel>

    <!-- Шаг 3: Белый список -->
    <StackPanel Visibility="{Binding IsStep3, Converter={StaticResource BooleanToVisibilityConverter}}">
      <TextBlock Text="Шаг 3 — Кого никогда не блокировать" FontWeight="600" Margin="0,0,0,8"/>
      <CheckBox Content="Локальная сеть (192.168.x.x, 10.x.x.x)" IsChecked="{Binding WhitelistLan}"/>
      <CheckBox Content="Этот компьютер (127.0.0.1)" IsChecked="{Binding WhitelistLocalhost}" Margin="0,4"/>
      <Grid Margin="0,8,0,0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <ui:TextBox Grid.Column="0" PlaceholderText="Свой IP, напр. 93.184.216.34"
                    Text="{Binding ExtraWhitelistIp, Mode=TwoWay}"/>
        <ui:Button Grid.Column="1" Content="+" Margin="8,0,0,0"
                   Command="{Binding AddExtraIpCommand}"/>
      </Grid>
      <ListBox ItemsSource="{Binding ExtraIps}" MaxHeight="100" Margin="0,8,0,0"/>
      <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
        <ui:Button Content="← Назад" Appearance="Transparent"
                   Command="{Binding PrevStepCommand}"/>
        <ui:Button Content="Далее →" Appearance="Primary" Margin="8,0,0,0"
                   Command="{Binding NextStepCommand}"/>
      </StackPanel>
    </StackPanel>
  </StackPanel>
</ui:ContentDialog>
```

UX-детали:
- Слайдер + `NumberBox` синхронизированы двусторонним биндингом — доступность (мышь + клавиатура).
- Пресеты одним кликом выставляют пару (порог + длительность).
- Превью-текст перефразирует правило человеческим языком — снижает страх «сломать сеть».
- Кнопка Primary меняет текст по шагу: «Далее» → «Включить защиту».

### User Journey Варианта 3
1. Клик «Настроить Defender» → модальный визард, фокус на слайдере.
2. Двигает слайдер до 50/мин → превью обновляется живьём.
3. Выбирает «1 час», отмечает «Не блокировать локальную сеть».
4. «Включить защиту» → диалог закрывается, Toast «Port Defender включён: 50/мин, бан 1 ч», тумблер в родителе переключается.

---

## ВАРИАНТ 4. Shield Badges в Диспетчере задач

**Идея:** колонка-щит в списке процессов (рядом с CPU/Сеть), hover-flyout с быстрыми действиями. Видно угрозу не открывая никаких окон.

Предполагается таблица процессов; добавить колонку `40px`:

```xml
<!-- В шапке таблицы процессов (MainWindow / TaskManager page) -->
<TextBlock Grid.Column="5" HorizontalAlignment="Center"
           ToolTip="Состояние сетевой защиты">
  <ui:SymbolIcon Symbol="Shield24" FontSize="14" Foreground="#94A3B8"/>
</TextBlock>

<!-- В строке процесса -->
<Border Grid.Column="5" Width="26" Height="26" CornerRadius="13"
        HorizontalAlignment="Center" VerticalAlignment="Center"
        BorderThickness="1" Cursor="Hand"
        MouseLeftButtonDown="OnShieldBadgeClick"
        ToolTip="{Binding ShieldTooltip}">
  <Border.Style>
    <Style TargetType="Border">
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="BorderBrush" Value="Transparent"/>
      <Style.Triggers>
        <DataTrigger Binding="{Binding ShieldState}" Value="Protected">
          <Setter Property="Background" Value="#1A3B82F6"/>
          <Setter Property="BorderBrush" Value="#603B82F6"/>
        </DataTrigger>
        <DataTrigger Binding="{Binding ShieldState}" Value="Attacked">
          <Setter Property="Background" Value="#26EF4444"/>
          <Setter Property="BorderBrush" Value="#80EF4444"/>
        </DataTrigger>
      </Style.Triggers>
    </Style>
  </Border.Style>
  <ui:SymbolIcon FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center">
    <ui:SymbolIcon.Style>
      <Style TargetType="ui:SymbolIcon">
        <Setter Property="Symbol" Value="Shield24"/>
        <Setter Property="Foreground" Value="#64748B"/>
        <Style.Triggers>
          <DataTrigger Binding="{Binding ShieldState}" Value="Protected">
            <Setter Property="Symbol" Value="ShieldCheckmark24"/>
            <Setter Property="Foreground" Value="#60A5FA"/>
          </DataTrigger>
          <DataTrigger Binding="{Binding ShieldState}" Value="Attacked">
            <Setter Property="Symbol" Value="ShieldError24"/>
            <Setter Property="Foreground" Value="#EF4444"/>
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </ui:SymbolIcon.Style>
  </ui:SymbolIcon>
  <!-- Flyout -->
  <Border.ToolTip>
    <ToolTip Placement="Right" MaxWidth="300" Background="Transparent" BorderThickness="0">
      <Border Background="#F0101721" BorderBrush="#26FFFFFF"
              BorderThickness="1" CornerRadius="10" Padding="12">
        <StackPanel>
          <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
            <ui:SymbolIcon Symbol="Shield24" Foreground="#60A5FA" Margin="0,0,6,0"/>
            <TextBlock Text="{Binding ProcessName}" FontWeight="700" Foreground="White"/>
          </StackPanel>
          <TextBlock Text="{Binding ShieldTooltip}" FontSize="11"
                     Foreground="#CBD5E1" TextWrapping="Wrap" Margin="0,0,0,8"/>
          <StackPanel Orientation="Horizontal">
            <ui:Button Content="Соединения" Appearance="Secondary" FontSize="11"
                       Padding="8,4" Margin="0,0,6,0"
                       Command="{Binding OpenConnectionsCommand}"/>
            <ui:Button Content="Заблокировать IP" Appearance="Secondary" FontSize="11"
                       Padding="8,4"
                       Command="{Binding QuickBlockCommand}"/>
          </StackPanel>
        </StackPanel>
      </Border>
    </ToolTip>
  </Border.ToolTip>
</Border>
```

Состояния бейджа:

| `ShieldState` | Вид | Tooltip |
|---|---|---|
| `None` | Серый контур щита | «Защита не настроена — клик для настройки» |
| `Protected` | Синяя заливка + ✅ | «Под защитой • 2 бана активно» |
| `Attacked` | Красная заливка + пульсация `DoubleAnimation` opacity | «⚠ Атака! 120 попыток/мин на :3389» |

Клик по бейджу: если `None` → открыть Визард (Вариант 3); иначе → открыть `ProcessConnectionsWindow` на вкладке Firewall.

### User Journey Варианта 4
1. Пользователь скроллит Диспетчер задач → у `mysqld.exe` красный пульсирующий щит.
2. Наводит мышь → flyout: «Атака! 120 попыток/мин», две кнопки.
3. Клик «Соединения» → детальный разбор (переход в Вариант 1). Клик «Заблокировать IP» → мгновенный бан топ-нарушителя.

---

## Сравнение и рекомендация

|  | Сложность | Клики до бана | Обзорность | Риск |
|---|---|---|---|---|
| 1. Инспектор | ★☆☆ (1 файл) | 2 | Низкая | Минимальный |
| 2. Hub | ★★★ (новая страница + VM) | 2 | Максимальная | Средний |
| 3. Wizard | ★★☆ (1 диалог) | 3 (но guided) | — | Низкий, снижает ошибки |
| 4. Badges | ★★☆ (колонка + tooltip) | 1–2 | Средняя (ambient) | Нужен троттлинг опроса Firewall |

**Рекомендуемый порядок внедрения:** **1 → 3 → 4 → 2**. Сначала контекстные действия (быстрая ценность), затем визард (безопасная настройка), затем ambient-индикаторы, и только потом тяжёлый Hub — он переиспользует VM и DTO первых трёх.

**Минимальный code-behind для Варианта 1** (в `ProcessConnectionsWindow.xaml.cs`):

```csharp
private async void OnBlockIpClick(object s, RoutedEventArgs e) {
  if (GridConnections.SelectedItem is SocketRow row)
    await _pipeClient.BlockIpAsync(row.RemoteAddress); // → FirewallOperationResult
  await LoadConnectionsAsync();
}
private async void OnBlockPortClick(object s, RoutedEventArgs e) {
  if (GridConnections.SelectedItem is SocketRow row)
    await _pipeClient.BlockPortAsync(row.RemotePort, row.Protocol);
  await LoadConnectionsAsync();
}
private void OnConfigureDefenderClick(object s, RoutedEventArgs e) {
  new PortDefenderWizard(_pipeClient) { Owner = this }.ShowDialog();
}
```

Все четыре варианта используют одни и те же `FirewallStatus` / `TempBanDto` / кисти — при поэтапном внедрении ничего не придётся переписывать.
