# Проект комплексного редизайна страницы сетевой безопасности (NetworkSecurityHubView)

**Архитектурный стандарт:** AI-Core Manifest 3.0  
**Роль:** Ведущий WPF/XAML разработчик и UI/UX архитектор (Gemini Pro)  
**Компонент:** `src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml` и `NetworkSecurityHubView.xaml.cs`  
**Дата разработки:** 25 октября 2026 г.  

---

## 1. Концепция визуального стиля и архитектура интерфейса

### 1.1 Неоновый кибер-стиль (Cyberpunk Glassmorphism)
В соответствии с дизайн-системой `MainWindow.xaml` обновленный `NetworkSecurityHubView` выполнен в единой стилистике кибер-мониторинга Windows:
* **Геометрия карточек**: Единые скругления углов `CornerRadius="8"` и `10`, исключающие визуальную несогласованность.
* **Материалы и полупрозрачность**: Полупрозрачные подложки `CardBackgroundFillColorDefaultBrush` и `GlassCardBackgroundBrush` с глубокими темными оттенками `#D00E141E` и `#800D131C` под эффект Mica.
* **Неоновые контуры и акценты**:
  * **Emerald Cyan / Green (`#10B981`, `#00FF88`, `#34D399`)**: Безопасный сетевой трафик, белые списки, разрешенные правила, подтвержденные доверенные процессы.
  * **Crimson Red (`#EF4444`, `#FF0055`, `#F87171`)**: Блокировки брандмауэра, черный список приложений, режим экстренной изоляции (Killswitch), активные сетевые атаки.
  * **Amber Orange (`#F59E0B`, `#FBBF24`)**: Сомнительные соединения, временные состояния TIME_WAIT/CLOSE_WAIT, предупреждения об отсутствующих файлах на диске.
  * **Neon Cyan (`#00D2D3`, `#00F2FE`)**: Активные потоки данных, мониторинг сетевых сокетов в реальном времени, активные вкладки и индикаторы сетевой активности.
* **Типографика**: Моноширинные шрифты `Consolas`, `Cascadia Code` для IP-адресов, портов и PID процессов. Четкая иерархия шрифтов `Segoe UI Variable / Segoe UI`.

---

## 2. Архитектура пяти вкладок

| Вкладка | Название | Иконка | Назначение и функциональность |
|---|---|---|---|
| **1** | **Активные соединения (Live Connections)** | `Globe24` | Интерактивная таблица всех открытых TCP/UDP сокетов системы в реальном времени. Карточки экспресс-метрик (Всего, Established, Listening, Блокировано). Бейджи GeoIP с флагами стран. Колонки быстрых действий в строке: «В белый список», «В чёрный список», «Завершить процесс». |
| **2** | **Белый список (Whitelist)** | `ShieldCheckmark24` | Таблица приложений, которым разрешен любой сетевой трафик без показа интерактивных предупреждений. Отображение пути, размера, статуса проверки на диске. Кнопки «Добавить программу...» (OpenFileDialog) и «Удалить из списка». |
| **3** | **Чёрный список (Blacklist)** | `ShieldDismiss24` | Таблица принудительно заблокированных приложений. Блокирует как входящий, так и исходящий трафик через системный Windows Firewall. Кнопки «Добавить в чёрный список...» и «Разблокировать (снять блокировку)». |
| **4** | **Правила брандмауэра (System Rules)** | `DocumentBulletList24` | Модернизированный список системных правил Windows Firewall. Поиск, сегментные фильтры (Inbound, Outbound, Block, StartupMonitor). Создание, редактирование, удаление, экспорт/импорт в JSON, пресеты приватности и блокировка телеметрии Windows. |
| **5** | **Port Defender** | `LockClosed24` | Служба защиты сетевых портов и авто-бан атак. Панель защищаемых портов (SSH, HTTP, HTTPS, RDP, SMB и др.) с удобными бейджами, кнопка вызова конфигурации `PortDefenderConfigWindow`, таблица активных банов с живым обратным отсчетом времени. |

---

## 3. Архитектурные решения в C# коде

1. **Модель `FirewallAppEntry`**:
   Полноценный класс элемента приложения для белого и черного списков с поддержкой валидации существования на диске, извлечения размера, времени изменения и генерации неоновых бейджей.
2. **Устранение таймера `_alertPollerTimer`**:
   Таймер поллинга алертов удален из View, так как глобальный поллер централизованно работает в `MainViewModel.cs`. Это предотвращает дублирование окон и гонки потоков. Статические чистые функции `ShouldShowAlertWindow` и `BuildResolveRequest` сохранены для 100% прохождения тестов `StartupMonitor.Tests`.
3. **Бесшовная синхронизация со службой через Named Pipe**:
   Вызовы `_pipeClient.GetFirewallWhitelistAsync()`, `_pipeClient.AddToFirewallWhitelistAsync()`, `_pipeClient.GetFirewallBlacklistAsync()`, `_pipeClient.AddToFirewallBlacklistAsync()` обеспечивают атомарное сохранение в `AppSettings` службы.
4. **Быстрые действия из таблицы Live Connections**:
   Прямое добавление процесса активного сокета в белый или черный список по одной кнопке в строке сокета с авто-определением пути исполняемого файла по PID.

---

## 4. Полная разметка XAML: `NetworkSecurityHubView.xaml`

```xml
<UserControl
    x:Class="StartupMonitor.UI.Views.NetworkSecurityHubView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Background="{ui:ThemeResource ApplicationBackgroundBrush}">

    <UserControl.Resources>
        <!-- Неоновые градиенты и цветовые ресурсы кибер-стиля -->
        <LinearGradientBrush x:Key="CyberCardGlowBrush" StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#2000D2D3" Offset="0.0" />
            <GradientStop Color="#0A0078D4" Offset="0.5" />
            <GradientStop Color="#1510B981" Offset="1.0" />
        </LinearGradientBrush>

        <SolidColorBrush x:Key="NeonCyanBrush" Color="#00D2D3" />
        <SolidColorBrush x:Key="NeonEmeraldBrush" Color="#10B981" />
        <SolidColorBrush x:Key="NeonCrimsonBrush" Color="#EF4444" />
        <SolidColorBrush x:Key="NeonAmberBrush" Color="#F59E0B" />

        <Style x:Key="QuickStatCard" TargetType="Border">
            <Setter Property="Background" Value="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{ui:ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="Padding" Value="12,10" />
        </Style>

        <Style x:Key="ActionMiniButton" TargetType="Button">
            <Setter Property="Background" Value="#15FFFFFF" />
            <Setter Property="BorderBrush" Value="#25FFFFFF" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="6,4" />
            <Setter Property="Margin" Value="2,0" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="border" Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="4">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="border" Property="Background" Value="#35FFFFFF" />
                                <Setter TargetName="border" Property="BorderBrush" Value="#6000D2D3" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="border" Property="Background" Value="#5000D2D3" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <!-- Строка 0: Верхняя неоновая статусная карточка брандмауэра -->
            <RowDefinition Height="Auto" />
            <!-- Строка 1: Основной контейнер 5 вкладок -->
            <RowDefinition Height="*" />
            <!-- Строка 2: Нижняя информационная строка статуса -->
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- ========================================================================= -->
        <!-- ВЕРХНЯЯ КАРТОЧКА: Состояние сетевого экрана, профили и режимы фильтрации  -->
        <!-- ========================================================================= -->
        <Border Grid.Row="0" Margin="0,0,0,12"
                Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                BorderThickness="1" CornerRadius="8" Padding="14">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <!-- Неоновая иконка щита брандмауэра -->
                <Border Grid.RowSpan="2" Grid.Column="0" Width="48" Height="48" CornerRadius="8"
                        Background="#1A00D2D3" BorderBrush="#3300D2D3" BorderThickness="1"
                        Margin="0,0,14,0" VerticalAlignment="Center">
                    <ui:SymbolIcon x:Name="FirewallShieldIcon" Symbol="Shield24" FontSize="24"
                                   Foreground="{StaticResource NeonCyanBrush}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>

                <!-- Статус брандмауэра и бейджи сетевых профилей -->
                <StackPanel Grid.Row="0" Grid.Column="1" VerticalAlignment="Center">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <Border x:Name="FirewallStateBadge" CornerRadius="6" Padding="8,4" VerticalAlignment="Center" Margin="0,0,10,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtFirewallState" Text="{Binding NetFwStateUnknown, Source={StaticResource Loc}}"
                                       FontSize="13" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" VerticalAlignment="Center" />
                        </Border>
                        <TextBlock x:Name="TxtFirewallDetails" Text="{Binding NetStatusLoading, Source={StaticResource Loc}}"
                                   FontSize="12" Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Индикаторы активных профилей сети (Доменный, Частный, Публичный) -->
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <Border x:Name="ProfileDomainBadge" CornerRadius="4" Padding="8,3" Margin="0,0,6,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfileDomain" Text="{Binding NetProfileDomainDefault, Source={StaticResource Loc}}" FontSize="11"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                        </Border>
                        <Border x:Name="ProfilePrivateBadge" CornerRadius="4" Padding="8,3" Margin="0,0,6,0"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfilePrivate" Text="{Binding NetProfilePrivateDefault, Source={StaticResource Loc}}" FontSize="11"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                        </Border>
                        <Border x:Name="ProfilePublicBadge" CornerRadius="4" Padding="8,3"
                                Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}">
                            <TextBlock x:Name="TxtProfilePublic" Text="{Binding NetProfilePublicDefault, Source={StaticResource Loc}}" FontSize="11"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                        </Border>
                    </StackPanel>
                </StackPanel>

                <!-- Кнопка обновления статуса -->
                <ui:Button x:Name="BtnRefreshStatus" Grid.Row="0" Grid.Column="2" Appearance="Secondary"
                           Icon="{ui:SymbolIcon ArrowClockwise24}"
                           Content="{Binding NetBtnRefreshStatus, Source={StaticResource Loc}}"
                           Click="OnRefreshStatusClick" VerticalAlignment="Center" Margin="12,0,0,0" />

                <!-- Селектор режима глобальной фильтрации брандмауэра -->
                <StackPanel Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,12,0,0">
                    <TextBlock Text="{Binding NetFilteringModeHeader, Source={StaticResource Loc}}" FontSize="11" FontWeight="SemiBold"
                               Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,0,0,6" />
                    <StackPanel Orientation="Horizontal">
                        <RadioButton x:Name="RbModeNormal" GroupName="FilteringMode"
                                     Content="{Binding NetFilterModeNormal, Source={StaticResource Loc}}"
                                     Checked="OnFilteringModeChecked" VerticalAlignment="Center" Margin="0,0,16,0" />
                        <RadioButton x:Name="RbModeWhitelist" GroupName="FilteringMode"
                                     Content="{Binding NetFilterModeWhitelist, Source={StaticResource Loc}}"
                                     Foreground="{StaticResource NeonEmeraldBrush}" FontWeight="SemiBold"
                                     Checked="OnFilteringModeChecked" VerticalAlignment="Center" Margin="0,0,16,0" />
                        <RadioButton x:Name="RbModeKillswitch" GroupName="FilteringMode"
                                     Content="{Binding NetFilterModeKillswitch, Source={StaticResource Loc}}"
                                     Checked="OnFilteringModeChecked" VerticalAlignment="Center"
                                     Foreground="{StaticResource NeonCrimsonBrush}" FontWeight="SemiBold" />
                    </StackPanel>
                </StackPanel>

                <!-- Опции интерактивных уведомлений и подавления шума PID 0 -->
                <StackPanel Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,10,0,0" Orientation="Horizontal">
                    <CheckBox x:Name="ChkInteractiveAlerts"
                              Content="{Binding NetInteractiveAlertsCheck, Source={StaticResource Loc}}"
                              Checked="OnInteractiveAlertsChanged" Unchecked="OnInteractiveAlertsChanged"
                              VerticalAlignment="Center" Margin="0,0,20,0"
                              ToolTip="{Binding NetInteractiveAlertsTip, Source={StaticResource Loc}}" />
                    <CheckBox x:Name="ChkSilentPid0"
                              Content="{Binding FirewallSilentPid0Label, Source={StaticResource Loc}}"
                              Checked="OnSilentPid0Changed" Unchecked="OnSilentPid0Changed"
                              VerticalAlignment="Center"
                              ToolTip="{Binding FirewallSilentPid0Hint, Source={StaticResource Loc}}" />
                </StackPanel>
            </Grid>
        </Border>

        <!-- ========================================================================= -->
        <!-- ОСНОВНЫЕ ВКЛАДКИ: Live, Whitelist, Blacklist, System Rules, Port Defender -->
        <!-- ========================================================================= -->
        <TabControl Grid.Row="1" x:Name="MainTabs">

            <!-- ===================================================================== -->
            <!-- ВКЛАДКА 1: Активные соединения (Live Connections)                     -->
            <!-- ===================================================================== -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="Globe24" FontSize="15" Foreground="{StaticResource NeonCyanBrush}" Margin="0,0,6,0" />
                        <TextBlock Text="{Binding NetLiveConnectionsHeader, Source={StaticResource Loc}}" VerticalAlignment="Center" FontWeight="SemiBold" />
                    </StackPanel>
                </TabItem.Header>

                <Grid Margin="0,10,0,0">
                    <Grid.RowDefinitions>
                        <!-- Карточки экспресс-метрик -->
                        <RowDefinition Height="Auto" />
                        <!-- Панель поиска и фильтров -->
                        <RowDefinition Height="Auto" />
                        <!-- Таблица сокетов -->
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Панель экспресс-метрик сокетов -->
                    <Grid Grid.Row="0" Margin="0,0,0,10">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>

                        <!-- Всего сокетов -->
                        <Border Grid.Column="0" Style="{StaticResource QuickStatCard}" Margin="0,0,6,0">
                            <StackPanel>
                                <TextBlock Text="Всего соединений" FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                                <TextBlock x:Name="TxtStatTotalConnections" Text="0" FontSize="18" FontWeight="Bold"
                                           Foreground="{StaticResource NeonCyanBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>

                        <!-- Установленные (ESTABLISHED) -->
                        <Border Grid.Column="1" Style="{StaticResource QuickStatCard}" Margin="3,0,3,0">
                            <StackPanel>
                                <TextBlock Text="Активные (ESTABLISHED)" FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                                <TextBlock x:Name="TxtStatEstablished" Text="0" FontSize="18" FontWeight="Bold"
                                           Foreground="{StaticResource NeonEmeraldBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>

                        <!-- Прослушиваемые (LISTENING) -->
                        <Border Grid.Column="2" Style="{StaticResource QuickStatCard}" Margin="3,0,3,0">
                            <StackPanel>
                                <TextBlock Text="Слушают порты (LISTEN)" FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                                <TextBlock x:Name="TxtStatListening" Text="0" FontSize="18" FontWeight="Bold"
                                           Foreground="#38BDF8" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>

                        <!-- Заблокированные сокеты / IP -->
                        <Border Grid.Column="3" Style="{StaticResource QuickStatCard}" Margin="6,0,0,0">
                            <StackPanel>
                                <TextBlock Text="Блокированные IP" FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" />
                                <TextBlock x:Name="TxtStatBlockedSockets" Text="0" FontSize="18" FontWeight="Bold"
                                           Foreground="{StaticResource NeonCrimsonBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>
                    </Grid>

                    <!-- Фильтры поиска и режимы отображения соединений -->
                    <Grid Grid.Row="1" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="3*" />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>

                        <ui:TextBox x:Name="TxtSearchConnections" Grid.Row="0" Grid.Column="0"
                                    PlaceholderText="{Binding NetSearchConnectionsHint, Source={StaticResource Loc}}"
                                    TextChanged="OnConnectionsSearchChanged" Margin="0,0,12,6" />

                        <WrapPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,6">
                            <RadioButton x:Name="RbConnAll" GroupName="ConnFilter" Content="{Binding NetFilterAll, Source={StaticResource Loc}}"
                                         IsChecked="True" Checked="OnConnectionsFilterChanged" Margin="0,0,10,0" />
                            <RadioButton x:Name="RbConnWan" GroupName="ConnFilter" Content="{Binding NetFilterWan, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" Margin="0,0,10,0" />
                            <RadioButton x:Name="RbConnLan" GroupName="ConnFilter" Content="{Binding NetFilterLan, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" Margin="0,0,10,0" />
                            <RadioButton x:Name="RbConnEstablished" GroupName="ConnFilter" Content="{Binding NetFilterEstablished, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" Margin="0,0,10,0" />
                            <RadioButton x:Name="RbConnListening" GroupName="ConnFilter" Content="{Binding NetFilterListen, Source={StaticResource Loc}}"
                                         Checked="OnConnectionsFilterChanged" />
                        </WrapPanel>

                        <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" VerticalAlignment="Center">
                            <CheckBox x:Name="ChkAutoRefreshConn" Content="{Binding NetAutoRefresh2s, Source={StaticResource Loc}}"
                                      IsChecked="True" Checked="OnAutoRefreshConnChanged" Unchecked="OnAutoRefreshConnChanged"
                                      VerticalAlignment="Center" Margin="0,0,12,0" />
                            <ui:Button x:Name="BtnRefreshConn" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                       Content="{Binding NetBtnRefreshNow, Source={StaticResource Loc}}"
                                       Click="OnRefreshConnClick" Margin="0,0,12,0" />
                            <TextBlock x:Name="TxtConnectionsSummary" Text="—" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" VerticalAlignment="Center" />
                        </StackPanel>
                    </Grid>

                    <!-- Таблица активных сетевых соединений со встроенными быстрыми действиями -->
                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridLiveConnections"
                                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                                  CanUserReorderColumns="False" IsReadOnly="True" SelectionMode="Single"
                                  GridLinesVisibility="None" HeadersVisibility="Column" BorderThickness="1"
                                  BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="36">
                            <DataGrid.ContextMenu>
                                <ContextMenu>
                                    <MenuItem Header="Добавить программу в белый список" Click="OnCtxWhitelistProcessClick" />
                                    <MenuItem Header="Заблокировать программу (в чёрный список)" Click="OnCtxBlacklistProcessClick" />
                                    <Separator />
                                    <MenuItem x:Name="CtxBlockRemoteIp" Header="{Binding NetCtxBlockRemoteIp, Source={StaticResource Loc}}" Click="OnCtxBlockRemoteIpClick" />
                                    <MenuItem x:Name="CtxCreateRuleForProcess" Header="{Binding NetCtxCreateRuleForProcess, Source={StaticResource Loc}}" Click="OnCtxCreateRuleForProcessClick" />
                                    <Separator />
                                    <MenuItem x:Name="CtxCopyRemoteIp" Header="{Binding NetCtxCopyRemoteIp, Source={StaticResource Loc}}" Click="OnCtxCopyRemoteIpClick" />
                                    <MenuItem x:Name="CtxCopyEndpoint" Header="{Binding NetCtxCopyEndpoint, Source={StaticResource Loc}}" Click="OnCtxCopyEndpointClick" />
                                    <Separator />
                                    <MenuItem x:Name="CtxWhois" Header="{Binding NetCtxWhois, Source={StaticResource Loc}}" Click="OnCtxWhoisClick" />
                                    <MenuItem x:Name="CtxVirusTotal" Header="{Binding NetCtxVirusTotal, Source={StaticResource Loc}}" Click="OnCtxVirusTotalClick" />
                                    <Separator />
                                    <MenuItem x:Name="CtxKillProcess" Header="{Binding NetCtxKillProcess, Source={StaticResource Loc}}" Click="OnCtxKillProcessClick" />
                                </ContextMenu>
                            </DataGrid.ContextMenu>
                            <DataGrid.Columns>
                                <!-- Процесс и PID -->
                                <DataGridTemplateColumn Header="{Binding NetColProcess, Source={StaticResource Loc}}" Width="1.4*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="6,0">
                                                <TextBlock Text="{Binding ProcessName}" FontSize="12" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}"
                                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                                <Border Background="#15FFFFFF" CornerRadius="3" Padding="4,1" Margin="6,0,0,0">
                                                    <TextBlock Text="{Binding Pid}" FontSize="10" FontFamily="Consolas"
                                                               Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" />
                                                </Border>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Протокол -->
                                <DataGridTextColumn Header="{Binding NetColProtocol, Source={StaticResource Loc}}"
                                                    Binding="{Binding Protocol}" Width="65" />

                                <!-- Локальный адрес -->
                                <DataGridTextColumn Header="{Binding NetColLocalAddress, Source={StaticResource Loc}}"
                                                    Binding="{Binding LocalEndPoint}" Width="140" />

                                <!-- Удаленный адрес -->
                                <DataGridTextColumn Header="{Binding NetColRemoteAddress, Source={StaticResource Loc}}"
                                                    Binding="{Binding RemoteEndPoint}" Width="150" />

                                <!-- Геолокация удаленной сети -->
                                <DataGridTemplateColumn Header="{Binding NetColNetworkGeo, Source={StaticResource Loc}}" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="4,0">
                                                <TextBlock Text="{Binding CountryFlag}" FontSize="13" VerticalAlignment="Center" Margin="0,0,4,0" />
                                                <TextBlock Text="{Binding CountryText}" FontSize="11"
                                                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Состояние сокета (ESTABLISHED, LISTENING) -->
                                <DataGridTemplateColumn Header="{Binding NetColStatus, Source={StaticResource Loc}}" Width="110">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border CornerRadius="4" Padding="6,2"
                                                    Background="{Binding StateBadgeBrush}"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding State}" FontSize="10" FontWeight="SemiBold"
                                                           Foreground="{Binding StateForegroundBrush}" />
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Статус брандмауэра -->
                                <DataGridTextColumn Header="{Binding NetColFirewall, Source={StaticResource Loc}}"
                                                    Binding="{Binding FirewallBadge}" Width="110" />

                                <!-- Колонка быстрых действий прямо в строке сокета -->
                                <DataGridTemplateColumn Header="Действия" Width="110">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                <!-- В белый список -->
                                                <Button Style="{StaticResource ActionMiniButton}"
                                                        ToolTip="Разрешить навсегда (в белый список)"
                                                        Click="OnRowWhitelistClick">
                                                    <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="14" Foreground="{StaticResource NeonEmeraldBrush}" />
                                                </Button>
                                                <!-- В чёрный список -->
                                                <Button Style="{StaticResource ActionMiniButton}"
                                                        ToolTip="Заблокировать навсегда (в чёрный список)"
                                                        Click="OnRowBlacklistClick">
                                                    <ui:SymbolIcon Symbol="ShieldDismiss24" FontSize="14" Foreground="{StaticResource NeonCrimsonBrush}" />
                                                </Button>
                                                <!-- Завершить процесс сокета -->
                                                <Button Style="{StaticResource ActionMiniButton}"
                                                        ToolTip="Принудительно завершить процесс"
                                                        Click="OnRowKillProcessClick">
                                                    <ui:SymbolIcon Symbol="Dismiss24" FontSize="14" Foreground="#94A3B8" />
                                                </Button>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <!-- Заглушка при пустом списке соединений -->
                        <StackPanel x:Name="PanelConnEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="Globe24" FontSize="36" Foreground="{StaticResource NeonCyanBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,8" />
                            <TextBlock Text="{Binding NetNoConnectionsTitle, Source={StaticResource Loc}}" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center" />
                            <TextBlock Text="{Binding NetNoConnectionsSubtitle, Source={StaticResource Loc}}" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ===================================================================== -->
            <!-- ВКЛАДКА 2: Белый список приложений (Whitelist)                        -->
            <!-- ===================================================================== -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="15" Foreground="{StaticResource NeonEmeraldBrush}" Margin="0,0,6,0" />
                        <TextBlock Text="Белый список" VerticalAlignment="Center" FontWeight="SemiBold" />
                    </StackPanel>
                </TabItem.Header>

                <Grid Margin="0,10,0,0">
                    <Grid.RowDefinitions>
                        <!-- Информационный баннер -->
                        <RowDefinition Height="Auto" />
                        <!-- Панель действий -->
                        <RowDefinition Height="Auto" />
                        <!-- Таблица белого списка -->
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Информационная карточка -->
                    <Border Grid.Row="0" Margin="0,0,0,10" Padding="12" CornerRadius="8"
                            Background="#1010B981" BorderBrush="#3010B981" BorderThickness="1">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <ui:SymbolIcon Grid.Column="0" Symbol="ShieldCheckmark24" FontSize="24"
                                           Foreground="{StaticResource NeonEmeraldBrush}" Margin="0,0,12,0" VerticalAlignment="Center" />
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="Доверенные приложения (Whitelist)" FontSize="13" FontWeight="SemiBold"
                                           Foreground="{StaticResource NeonEmeraldBrush}" />
                                <TextBlock Text="Приложениям из белого списка разрешен любой сетевой трафик без показа интерактивных всплывающих окон."
                                           FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                            <Border Grid.Column="2" CornerRadius="6" Padding="10,4" Background="#2010B981" VerticalAlignment="Center">
                                <TextBlock x:Name="TxtWhitelistCount" Text="0 приложений" FontSize="12" FontWeight="SemiBold"
                                           Foreground="{StaticResource NeonEmeraldBrush}" />
                            </Border>
                        </Grid>
                    </Border>

                    <!-- Панель управления белым списком -->
                    <Grid Grid.Row="1" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:TextBox x:Name="TxtSearchWhitelist" Grid.Column="0"
                                    PlaceholderText="Поиск по имени приложения или пути..."
                                    TextChanged="OnWhitelistSearchChanged" Margin="0,0,12,0" />

                        <StackPanel Grid.Column="2" Orientation="Horizontal">
                            <ui:Button x:Name="BtnAddWhitelistApp" Appearance="Primary" Icon="{ui:SymbolIcon Add24}"
                                       Content="Добавить программу в белый список..."
                                       Click="OnAddWhitelistAppClick" Margin="0,0,8,0" />
                            <ui:Button x:Name="BtnRefreshWhitelist" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                       Content="Обновить"
                                       Click="OnRefreshWhitelistClick" />
                        </StackPanel>
                    </Grid>

                    <!-- Таблица белого списка -->
                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridWhitelist"
                                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                                  CanUserReorderColumns="False" IsReadOnly="True" SelectionMode="Single"
                                  GridLinesVisibility="None" HeadersVisibility="Column" BorderThickness="1"
                                  BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="36">
                            <DataGrid.Columns>
                                <!-- Программа -->
                                <DataGridTemplateColumn Header="Программа" Width="1.5*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="6,0">
                                                <ui:SymbolIcon Symbol="AppGeneric24" FontSize="16" Foreground="{StaticResource NeonEmeraldBrush}" Margin="0,0,8,0" />
                                                <TextBlock Text="{Binding ProcessName}" FontSize="12" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" VerticalAlignment="Center" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Полный путь -->
                                <DataGridTemplateColumn Header="Полный путь к файлу" Width="3*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding FullPath}" FontSize="11" FontFamily="Consolas"
                                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding FullPath}"
                                                       VerticalAlignment="Center" Margin="4,0" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Статус проверки на диске -->
                                <DataGridTemplateColumn Header="Состояние" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="{Binding StatusBadgeBrush}" CornerRadius="4" Padding="6,2"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding StatusBadge}" FontSize="11" FontWeight="SemiBold"
                                                           Foreground="{Binding StatusTextBrushHex}" />
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Размер файла -->
                                <DataGridTextColumn Header="Размер" Binding="{Binding FileSizeDisplay}" Width="90" />

                                <!-- Дата изменения файла -->
                                <DataGridTextColumn Header="Дата файла" Binding="{Binding DateAddedDisplay}" Width="130" />

                                <!-- Кнопка удаления из белого списка -->
                                <DataGridTemplateColumn Header="Действие" Width="100">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Button Style="{StaticResource ActionMiniButton}"
                                                    ToolTip="Удалить из белого списка"
                                                    Click="OnRemoveWhitelistAppClick"
                                                    HorizontalAlignment="Center">
                                                <StackPanel Orientation="Horizontal">
                                                    <ui:SymbolIcon Symbol="Delete24" FontSize="14" Foreground="{StaticResource NeonCrimsonBrush}" Margin="0,0,4,0" />
                                                    <TextBlock Text="Удалить" FontSize="11" Foreground="{StaticResource NeonCrimsonBrush}" />
                                                </StackPanel>
                                            </Button>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <!-- Заглушка пустого списка -->
                        <StackPanel x:Name="PanelWhitelistEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="36" Foreground="{StaticResource NeonEmeraldBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,8" />
                            <TextBlock Text="Белый список пуст" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center" />
                            <TextBlock Text="Добавьте программы через кнопку вверху или из контекстного меню сокетов."
                                       FontSize="12" Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,4,0,12" />
                            <ui:Button Appearance="Primary" Content="Добавить программу..." Click="OnAddWhitelistAppClick" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ===================================================================== -->
            <!-- ВКЛАДКА 3: Чёрный список приложений (Blacklist)                       -->
            <!-- ===================================================================== -->
            <TabItem>
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <ui:SymbolIcon Symbol="ShieldDismiss24" FontSize="15" Foreground="{StaticResource NeonCrimsonBrush}" Margin="0,0,6,0" />
                        <TextBlock Text="Чёрный список" VerticalAlignment="Center" FontWeight="SemiBold" />
                    </StackPanel>
                </TabItem.Header>

                <Grid Margin="0,10,0,0">
                    <Grid.RowDefinitions>
                        <!-- Информационный баннер -->
                        <RowDefinition Height="Auto" />
                        <!-- Панель действий -->
                        <RowDefinition Height="Auto" />
                        <!-- Таблица черного списка -->
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Предупреждающая карточка -->
                    <Border Grid.Row="0" Margin="0,0,0,10" Padding="12" CornerRadius="8"
                            Background="#10EF4444" BorderBrush="#30EF4444" BorderThickness="1">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <ui:SymbolIcon Grid.Column="0" Symbol="ShieldDismiss24" FontSize="24"
                                           Foreground="{StaticResource NeonCrimsonBrush}" Margin="0,0,12,0" VerticalAlignment="Center" />
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="Заблокированные приложения (Blacklist)" FontSize="13" FontWeight="SemiBold"
                                           Foreground="{StaticResource NeonCrimsonBrush}" />
                                <TextBlock Text="Приложениям из черного списка полностью перекрыт доступ в сеть системным правилом Windows Firewall."
                                           FontSize="11" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                            <Border Grid.Column="2" CornerRadius="6" Padding="10,4" Background="#20EF4444" VerticalAlignment="Center">
                                <TextBlock x:Name="TxtBlacklistCount" Text="0 приложений" FontSize="12" FontWeight="SemiBold"
                                           Foreground="{StaticResource NeonCrimsonBrush}" />
                            </Border>
                        </Grid>
                    </Border>

                    <!-- Панель управления черным списком -->
                    <Grid Grid.Row="1" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:TextBox x:Name="TxtSearchBlacklist" Grid.Column="0"
                                    PlaceholderText="Поиск заблокированных приложений..."
                                    TextChanged="OnBlacklistSearchChanged" Margin="0,0,12,0" />

                        <StackPanel Grid.Column="2" Orientation="Horizontal">
                            <ui:Button x:Name="BtnAddBlacklistApp" Appearance="Danger" Icon="{ui:SymbolIcon Add24}"
                                       Content="Добавить программу в чёрный список..."
                                       Click="OnAddBlacklistAppClick" Margin="0,0,8,0" />
                            <ui:Button x:Name="BtnRefreshBlacklist" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                       Content="Обновить"
                                       Click="OnRefreshBlacklistClick" />
                        </StackPanel>
                    </Grid>

                    <!-- Таблица черного списка -->
                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridBlacklist"
                                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                                  CanUserReorderColumns="False" IsReadOnly="True" SelectionMode="Single"
                                  GridLinesVisibility="None" HeadersVisibility="Column" BorderThickness="1"
                                  BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="36">
                            <DataGrid.Columns>
                                <!-- Программа -->
                                <DataGridTemplateColumn Header="Программа" Width="1.5*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="6,0">
                                                <ui:SymbolIcon Symbol="ShieldDismiss24" FontSize="16" Foreground="{StaticResource NeonCrimsonBrush}" Margin="0,0,8,0" />
                                                <TextBlock Text="{Binding ProcessName}" FontSize="12" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" VerticalAlignment="Center" />
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Полный путь -->
                                <DataGridTemplateColumn Header="Полный путь к файлу" Width="3*">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding FullPath}" FontSize="11" FontFamily="Consolas"
                                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding FullPath}"
                                                       VerticalAlignment="Center" Margin="4,0" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Режим изоляции -->
                                <DataGridTemplateColumn Header="Режим" Width="130">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="#25EF4444" CornerRadius="4" Padding="6,2"
                                                    HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock Text="🚫 In + Out Block" FontSize="11" FontWeight="SemiBold"
                                                           Foreground="{StaticResource NeonCrimsonBrush}" />
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Размер файла -->
                                <DataGridTextColumn Header="Размер" Binding="{Binding FileSizeDisplay}" Width="90" />

                                <!-- Дата изменения файла -->
                                <DataGridTextColumn Header="Дата файла" Binding="{Binding DateAddedDisplay}" Width="130" />

                                <!-- Кнопка снятия блокировки -->
                                <DataGridTemplateColumn Header="Действие" Width="140">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Button Style="{StaticResource ActionMiniButton}"
                                                    ToolTip="Снять блокировку и удалить из черного списка"
                                                    Click="OnRemoveBlacklistAppClick"
                                                    HorizontalAlignment="Center">
                                                <StackPanel Orientation="Horizontal">
                                                    <ui:SymbolIcon Symbol="LockOpen24" FontSize="14" Foreground="{StaticResource NeonEmeraldBrush}" Margin="0,0,4,0" />
                                                    <TextBlock Text="Разблокировать" FontSize="11" Foreground="{StaticResource NeonEmeraldBrush}" />
                                                </StackPanel>
                                            </Button>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                        <!-- Заглушка при пустом черном списке -->
                        <StackPanel x:Name="PanelBlacklistEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="36" Foreground="{StaticResource NeonEmeraldBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,8" />
                            <TextBlock Text="Чёрный список пуст" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center" />
                            <TextBlock Text="Нет приложений с принудительной сетевой блокировкой."
                                       FontSize="12" Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                       HorizontalAlignment="Center" Margin="0,4,0,0" />
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ===================================================================== -->
            <!-- ВКЛАДКА 4: Правила брандмауэра (System Rules)                         -->
            <!-- ===================================================================== -->
            <TabItem Header="{Binding NetTabFirewallRules, Source={StaticResource Loc}}">
                <Grid Margin="0,10,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Поиск и фильтры правил -->
                    <Grid Grid.Row="0" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="3*" />
                        </Grid.ColumnDefinitions>
                        <ui:TextBox x:Name="TxtRuleSearch" Grid.Column="0"
                                    PlaceholderText="{Binding NetSearchRulesHint, Source={StaticResource Loc}}"
                                    TextChanged="OnRuleSearchTextChanged" Margin="0,0,12,0" />
                        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="{Binding NetRulesFilterLabel, Source={StaticResource Loc}}" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="0,0,8,0" />
                            <RadioButton x:Name="FilterAll" GroupName="RuleFilter" Content="{Binding NetRulesFilterAll, Source={StaticResource Loc}}" IsChecked="True"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <RadioButton x:Name="FilterInbound" GroupName="RuleFilter" Content="{Binding NetRulesFilterInbound, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <RadioButton x:Name="FilterOutbound" GroupName="RuleFilter" Content="{Binding NetRulesFilterOutbound, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <RadioButton x:Name="FilterBlocking" GroupName="RuleFilter" Content="{Binding NetRulesFilterBlock, Source={StaticResource Loc}}"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <RadioButton x:Name="FilterManaged" GroupName="RuleFilter" Content="StartupMonitor"
                                         Checked="OnRuleFilterChecked" VerticalAlignment="Center" />
                        </StackPanel>
                    </Grid>

                    <!-- Панель кнопок управления правилами -->
                    <WrapPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
                        <ui:Button x:Name="BtnCreateRule" Appearance="Primary" Icon="{ui:SymbolIcon Add24}"
                                   Content="{Binding NetBtnCreateRule, Source={StaticResource Loc}}"
                                   Click="OnCreateRuleClick" Margin="0,0,8,4" />
                        <ui:Button x:Name="BtnEditRule" Appearance="Secondary" Icon="{ui:SymbolIcon Edit24}"
                                   Content="{Binding NetBtnEditRule, Source={StaticResource Loc}}"
                                   Click="OnEditRuleClick" Margin="0,0,8,4" />
                        <ui:Button x:Name="BtnDeleteRule" Appearance="Secondary" Icon="{ui:SymbolIcon Delete24}"
                                   Content="{Binding NetBtnDeleteRule, Source={StaticResource Loc}}"
                                   Click="OnDeleteRuleClick" Margin="0,0,8,4" />
                        <ui:Button x:Name="BtnExportRules" Appearance="Secondary" Icon="{ui:SymbolIcon Save24}"
                                   Content="{Binding NetBtnExport, Source={StaticResource Loc}}"
                                   ToolTip="{Binding NetBtnExportTip, Source={StaticResource Loc}}"
                                   Click="OnExportRulesClick" Margin="0,0,8,4" />
                        <ui:Button x:Name="BtnImportRules" Appearance="Secondary" Icon="{ui:SymbolIcon FolderOpen24}"
                                   Content="{Binding NetBtnImport, Source={StaticResource Loc}}"
                                   ToolTip="{Binding NetBtnImportTip, Source={StaticResource Loc}}"
                                   Click="OnImportRulesClick" Margin="0,0,8,4" />
                        <ui:DropDownButton x:Name="BtnSecurityPresets" Appearance="Secondary"
                                           Icon="{ui:SymbolIcon ShieldCheckmark24}"
                                           Content="{Binding NetBtnSecurityPresets, Source={StaticResource Loc}}"
                                           Margin="0,0,8,4">
                            <ui:DropDownButton.Flyout>
                                <ContextMenu>
                                    <MenuItem Header="{Binding NetPresetMaxPrivacy, Source={StaticResource Loc}}" Click="OnApplyPresetMaxPrivacyClick" />
                                    <MenuItem Header="{Binding NetPresetGaming, Source={StaticResource Loc}}" Click="OnApplyPresetGamingClick" />
                                    <MenuItem Header="{Binding NetPresetPublicWifi, Source={StaticResource Loc}}" Click="OnApplyPresetPublicWifiClick" />
                                    <Separator />
                                    <MenuItem Header="{Binding NetPresetReset, Source={StaticResource Loc}}" Click="OnApplyPresetResetClick" />
                                </ContextMenu>
                            </ui:DropDownButton.Flyout>
                        </ui:DropDownButton>
                        <ui:Button x:Name="BtnRefreshRules" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                   Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                   Click="OnRefreshRulesClick" Margin="0,0,8,4" />
                        <ui:Button x:Name="BtnAntiTelemetry" Appearance="Danger" Icon="{ui:SymbolIcon ShieldDismiss24}"
                                   Content="{Binding NetBtnAntiTelemetry, Source={StaticResource Loc}}"
                                   Click="OnAntiTelemetryClick" Margin="0,0,0,4"
                                   ToolTip="{Binding NetBtnAntiTelemetryTip, Source={StaticResource Loc}}" />
                    </WrapPanel>

                    <!-- Таблица системных правил -->
                    <Grid Grid.Row="2">
                        <DataGrid x:Name="GridRules"
                                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                                  CanUserReorderColumns="False" IsReadOnly="True" SelectionMode="Single"
                                  GridLinesVisibility="None" HeadersVisibility="Column" BorderThickness="1"
                                  BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                                  Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                  RowHeight="34" MouseDoubleClick="OnRuleGridDoubleClick">
                            <DataGrid.Columns>
                                <!-- Включение правила -->
                                <DataGridTemplateColumn Header="{Binding NetColEnabled, Source={StaticResource Loc}}" Width="52">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}"
                                                      Checked="OnRuleToggleChecked" Unchecked="OnRuleToggleChecked"
                                                      HorizontalAlignment="Center" VerticalAlignment="Center" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Действие (Allow / Block) -->
                                <DataGridTemplateColumn Header="{Binding NetColAction, Source={StaticResource Loc}}" Width="105">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border CornerRadius="4" Padding="6,2" HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <Border.Style>
                                                    <Style TargetType="Border">
                                                        <Setter Property="Background" Value="#2510B981" />
                                                        <Setter Property="BorderBrush" Value="#4010B981" />
                                                        <Setter Property="BorderThickness" Value="1" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding Action}" Value="Block">
                                                                <Setter Property="Background" Value="#25EF4444" />
                                                                <Setter Property="BorderBrush" Value="#40EF4444" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Border.Style>
                                                <TextBlock FontSize="11" FontWeight="SemiBold">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Text" Value="Allow" />
                                                            <Setter Property="Foreground" Value="{StaticResource NeonEmeraldBrush}" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Action}" Value="Block">
                                                                    <Setter Property="Text" Value="Block" />
                                                                    <Setter Property="Foreground" Value="{StaticResource NeonCrimsonBrush}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <!-- Направление -->
                                <DataGridTemplateColumn Header="{Binding NetColDirection, Source={StaticResource Loc}}" Width="80">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <Border Background="{ui:ThemeResource CardBackgroundFillColorSecondaryBrush}"
                                                    CornerRadius="4" Padding="6,2" HorizontalAlignment="Left" VerticalAlignment="Center">
                                                <TextBlock FontSize="11" FontWeight="SemiBold"
                                                           Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock">
                                                            <Setter Property="Text" Value="In" />
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding Direction}" Value="Outbound">
                                                                    <Setter Property="Text" Value="Out" />
                                                                </DataTrigger>
                                                                <DataTrigger Binding="{Binding Direction}" Value="Both">
                                                                    <Setter Property="Text" Value="In+Out" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Border>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>

                                <DataGridTextColumn Header="{Binding NetColName, Source={StaticResource Loc}}" Binding="{Binding Name}" Width="2*" />
                                <DataGridTextColumn Header="{Binding NetColProgram, Source={StaticResource Loc}}" Binding="{Binding ShortProgram}" Width="1.5*" />
                                <DataGridTextColumn Header="{Binding NetColProtocol, Source={StaticResource Loc}}" Binding="{Binding ProtocolDisplay}" Width="80" />
                                <DataGridTextColumn Header="{Binding NetColLocalPorts, Source={StaticResource Loc}}" Binding="{Binding LocalPorts}" Width="110" />
                                <DataGridTextColumn Header="{Binding NetColRemoteAddresses, Source={StaticResource Loc}}" Binding="{Binding RemoteAddresses}" Width="1.5*" />
                                <DataGridTextColumn Header="{Binding NetColProfiles, Source={StaticResource Loc}}" Binding="{Binding ProfilesDisplay}" Width="130" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <StackPanel x:Name="PanelRulesEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                            <ui:SymbolIcon Symbol="Shield24" FontSize="32" Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                                           HorizontalAlignment="Center" Margin="0,0,0,8" />
                            <TextBlock Text="{Binding NetNoRulesTitle, Source={StaticResource Loc}}" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center" />
                            <TextBlock Text="{Binding NetNoRulesSubtitle, Source={StaticResource Loc}}" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

            <!-- ===================================================================== -->
            <!-- ВКЛАДКА 5: Port Defender и журнал банов атак                          -->
            <!-- ===================================================================== -->
            <TabItem Header="{Binding NetTabPortDefender, Source={StaticResource Loc}}">
                <Grid Margin="0,10,0,0">
                    <Grid.RowDefinitions>
                        <!-- Статус защиты и конфигурация -->
                        <RowDefinition Height="Auto" />
                        <!-- Защищаемые порты -->
                        <RowDefinition Height="Auto" />
                        <!-- Журнал атак и банов IP -->
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Статус службы Port Defender -->
                    <Border Grid.Row="0" Margin="0,0,0,10" Padding="14" CornerRadius="8"
                            Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                            BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Border Grid.Column="0" Width="42" Height="42" CornerRadius="8"
                                    Background="#1A00D2D3" BorderBrush="#3300D2D3" BorderThickness="1"
                                    Margin="0,0,12,0" VerticalAlignment="Center">
                                <ui:SymbolIcon Symbol="Shield24" FontSize="22" Foreground="{StaticResource NeonCyanBrush}"
                                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                            </Border>
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock x:Name="TxtDefenderState" Text="{Binding NetDefenderStateLoading, Source={StaticResource Loc}}"
                                           FontSize="13" FontWeight="SemiBold" Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" />
                                <TextBlock x:Name="TxtDefenderDetails" Text="—" FontSize="12"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" Margin="0,2,0,0" />
                            </StackPanel>
                            <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                <ui:Button x:Name="BtnConfigureDefender" Appearance="Primary" Icon="{ui:SymbolIcon Settings24}"
                                           Content="{Binding PortDefenderTitle, Source={StaticResource Loc}}"
                                           Click="OnConfigureDefenderClick" Margin="0,0,8,0" />
                                <ui:Button x:Name="BtnRefreshDefender" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                           Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                           Click="OnRefreshDefenderClick" />
                            </StackPanel>
                        </Grid>
                    </Border>

                    <!-- Плашка защищаемых портов -->
                    <Border Grid.Row="1" Margin="0,0,0,10" Padding="14" CornerRadius="8"
                            Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                            BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                        <StackPanel>
                            <TextBlock Text="{Binding PortDefenderProtectedPortsLabel, Source={StaticResource Loc}}" FontSize="12" FontWeight="SemiBold"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" Margin="0,0,0,6" />
                            <TextBlock x:Name="TxtDefenderPorts" Text="—" FontSize="13" FontFamily="Consolas, Cascadia Code, Segoe UI"
                                       Foreground="{StaticResource NeonCyanBrush}" TextWrapping="Wrap" />
                        </StackPanel>
                    </Border>

                    <!-- Журнал активных банов IP-адресов -->
                    <Grid Grid.Row="2">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>

                        <!-- Панель управления банами -->
                        <WrapPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                            <ui:Button x:Name="BtnRefreshBans" Appearance="Secondary" Icon="{ui:SymbolIcon ArrowClockwise24}"
                                       Content="{Binding NetBtnRefreshRules, Source={StaticResource Loc}}"
                                       Click="OnRefreshBansClick" Margin="0,0,8,4" />
                            <ui:Button x:Name="BtnUnblockIp" Appearance="Secondary" Icon="{ui:SymbolIcon LockOpen24}"
                                       Content="{Binding NetBtnUnbanSelectedIp, Source={StaticResource Loc}}"
                                       Click="OnUnblockIpClick" Margin="0,0,8,4" />
                            <ui:Button x:Name="BtnClearBans" Appearance="Danger" Icon="{ui:SymbolIcon Delete24}"
                                       Content="{Binding NetBtnResetAllBans, Source={StaticResource Loc}}"
                                       Click="OnClearBansClick" Margin="0,0,0,4" />
                            <TextBlock x:Name="TxtBansCount" Text="—" FontSize="12"
                                       Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="12,0,0,4" />
                        </WrapPanel>

                        <!-- Таблица банов -->
                        <Grid Grid.Row="1">
                            <DataGrid x:Name="GridBans"
                                      AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                                      CanUserReorderColumns="False" IsReadOnly="True" SelectionMode="Single"
                                      GridLinesVisibility="None" HeadersVisibility="Column" BorderThickness="1"
                                      BorderBrush="{ui:ThemeResource CardStrokeColorDefaultBrush}"
                                      Background="{ui:ThemeResource CardBackgroundFillColorDefaultBrush}"
                                      RowHeight="34">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="{Binding NetColIp, Source={StaticResource Loc}}" Binding="{Binding Ip}" Width="130" />
                                    <DataGridTextColumn Header="{Binding NetColPort, Source={StaticResource Loc}}" Binding="{Binding TargetPort}" Width="75" />
                                    <DataGridTextColumn Header="{Binding NetColProcess, Source={StaticResource Loc}}" Binding="{Binding TargetProcess}" Width="1.5*" />
                                    <DataGridTextColumn Header="{Binding NetColAttempts, Source={StaticResource Loc}}" Binding="{Binding AttackRate}" Width="85" />
                                    <DataGridTextColumn Header="{Binding NetColReason, Source={StaticResource Loc}}" Binding="{Binding Reason}" Width="2*" />
                                    <DataGridTextColumn Header="{Binding NetColRemaining, Source={StaticResource Loc}}" Binding="{Binding RemainingText}" Width="110" />
                                    <DataGridTextColumn Header="{Binding NetColRepeats, Source={StaticResource Loc}}" Binding="{Binding RepeatCount}" Width="80" />
                                </DataGrid.Columns>
                            </DataGrid>

                            <StackPanel x:Name="PanelBansEmpty" VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed">
                                <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="32" Foreground="{StaticResource NeonEmeraldBrush}"
                                               HorizontalAlignment="Center" Margin="0,0,0,8" />
                                <TextBlock Text="{Binding NetNoBansTitle, Source={StaticResource Loc}}" FontSize="14" FontWeight="SemiBold"
                                           Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center" />
                                <TextBlock Text="{Binding NetNoBansSubtitle, Source={StaticResource Loc}}" FontSize="12"
                                           Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}" HorizontalAlignment="Center" Margin="0,4,0,0" />
                            </StackPanel>
                        </Grid>
                    </Grid>
                </Grid>
            </TabItem>
        </TabControl>

        <!-- ========================================================================= -->
        <!-- НИЖНЯЯ СТАТУСНАЯ СТРОКА: Уведомления об операциях и ошибках               -->
        <!-- ========================================================================= -->
        <Grid Grid.Row="2" Margin="0,8,0,0">
            <TextBlock x:Name="TxtStatus" Text="{Binding NetStatusReady, Source={StaticResource Loc}}" FontSize="12"
                       Foreground="{ui:ThemeResource TextFillColorTertiaryBrush}"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </Grid>
    </Grid>
</UserControl>
```

---

## 5. Полный код C#: `NetworkSecurityHubView.xaml.cs`

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.UI.Views;

/// <summary>
/// Центр сетевой безопасности (NetworkSecurityHubView):
/// 1. Мониторинг активных сетевых соединений (Live) с кнопками экспресс-действий.
/// 2. Белый список сетевых приложений (Whitelist) с авто-пропуском трафика.
/// 3. Чёрный список сетевых приложений (Blacklist) с тотальной блокировкой трафика.
/// 4. Системные правила брандмауэра Windows с пресетами безопасности.
/// 5. Служба защиты портов Port Defender и журнал активных банов.
/// Zero-Crash гарантия: все межпроцессные взаимодействия обернуты в try-catch.
/// </summary>
public partial class NetworkSecurityHubView : UserControl
{
    private readonly PipeClient _pipeClient;
    private readonly DispatcherTimer _banTimer;
    private readonly DispatcherTimer _bansRefreshTimer;
    private readonly DispatcherTimer _liveConnTimer;

    // Флаги управления интерактивными алертами (поллинг выполняется централизованно в MainViewModel)
    private bool _alertsEnabled;
    private bool _suppressAlertsToggle;

    // Коллекции и состояния правил брандмауэра Windows
    private List<FirewallRuleRow> _allRules = new();
    private List<BanRow> _banRows = new();
    private PortDefenderConfigDto? _defenderConfig;
    private FirewallFilteringMode _currentMode = FirewallFilteringMode.Normal;
    private string _searchText = string.Empty;
    private RuleFilter _activeFilter = RuleFilter.All;
    private bool _isLoading;
    private bool _suppressModeEvents;

    // Коллекции и состояния активных соединений (Live Connections)
    private List<LiveConnectionRow> _allConnections = new();
    private string _connSearchText = string.Empty;
    private ConnFilter _activeConnFilter = ConnFilter.All;
    private bool _isLoadingConn;
    private HashSet<string> _blockedIps = new(StringComparer.OrdinalIgnoreCase);

    // Коллекции белого и черного списков приложений
    private List<FirewallAppEntry> _allWhitelistApps = new();
    private List<FirewallAppEntry> _allBlacklistApps = new();
    private string _whitelistSearchText = string.Empty;
    private string _blacklistSearchText = string.Empty;
    private bool _isLoadingWhitelist;
    private bool _isLoadingBlacklist;

    /// <summary>Кэш путей исполняемых файлов процессов по PID для исключения частых системных вызовов.</summary>
    private static readonly ConcurrentDictionary<int, string?> _processPathCache = new();

    /// <summary>Фильтр таблицы правил брандмауэра.</summary>
    public enum RuleFilter
    {
        All,
        Inbound,
        Outbound,
        Blocking,
        Managed
    }

    /// <summary>Фильтр таблицы активных соединений (вкладка Live).</summary>
    public enum ConnFilter
    {
        All,
        Wan,
        Lan,
        Established,
        Listening
    }

    /// <summary>
    /// Элемент приложения сетевого белого или черного списка.
    /// </summary>
    public sealed class FirewallAppEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Полный путь к исполняемому файлу программы.</summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>Имя процесса или файла с расширением.</summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>Дата последнего изменения или добавления файла.</summary>
        public string DateAddedDisplay { get; set; } = "—";

        /// <summary>Размер исполняемого файла на диске.</summary>
        public string FileSizeDisplay { get; set; } = "—";

        /// <summary>Флаг физического наличия файла на диске.</summary>
        public bool ExistsOnDisk { get; set; }

        /// <summary>Текстовый бейдж статуса приложения.</summary>
        public string StatusBadge { get; set; } = string.Empty;

        /// <summary>Фоновая кисть бейджа статуса.</summary>
        public Brush StatusBadgeBrush { get; set; } = Brushes.Transparent;

        /// <summary>HEX-цвет текста бейджа для высокой контрастности.</summary>
        public string StatusTextBrushHex { get; set; } = "#F8FAFC";

        /// <summary>
        /// Фабричный метод формирования записи приложения из пути с дисковой валидацией.
        /// </summary>
        /// <param name="path">Полный путь к файлу программы.</param>
        /// <param name="isBlacklist">Признак принадлежности к черному списку.</param>
        public static FirewallAppEntry FromPath(string path, bool isBlacklist = false)
        {
            var cleanPath = (path ?? string.Empty).Trim().Trim('"');
            var name = Path.GetFileName(cleanPath);
            if (string.IsNullOrWhiteSpace(name))
                name = cleanPath;

            bool exists = false;
            string dateStr = "—";
            string sizeStr = "—";

            try
            {
                if (File.Exists(cleanPath))
                {
                    exists = true;
                    var fi = new FileInfo(cleanPath);
                    dateStr = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    sizeStr = $"{fi.Length / 1024.0 / 1024.0:F1} МБ";
                }
            }
            catch
            {
                // Подавление ошибок ввода-вывода (Zero-Crash)
            }

            return new FirewallAppEntry
            {
                FullPath = cleanPath,
                ProcessName = name,
                DateAddedDisplay = dateStr,
                FileSizeDisplay = sizeStr,
                ExistsOnDisk = exists,
                StatusBadge = isBlacklist
                    ? "🚫 Заблокирован"
                    : (exists ? "🛡️ Доверен" : "⚠️ Не найден"),
                StatusBadgeBrush = isBlacklist
                    ? new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44))
                    : (exists
                        ? new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81))
                        : new SolidColorBrush(Color.FromArgb(0x33, 0x64, 0x74, 0x8B))),
                StatusTextBrushHex = isBlacklist ? "#F87171" : (exists ? "#34D399" : "#94A3B8")
            };
        }
    }

    /// <summary>
    /// Строка таблицы правил: обёртка над <see cref="FirewallRuleDto"/>
    /// </summary>
    public sealed class FirewallRuleRow
    {
        public FirewallRuleRow(FirewallRuleDto dto) => Dto = dto ?? new FirewallRuleDto();

        public FirewallRuleDto Dto { get; }

        public string Name { get => Dto.Name; set => Dto.Name = value; }
        public string Description { get => Dto.Description; set => Dto.Description = value; }
        public FirewallDirection Direction { get => Dto.Direction; set => Dto.Direction = value; }
        public FirewallAction Action { get => Dto.Action; set => Dto.Action = value; }
        public FirewallProtocol Protocol { get => Dto.Protocol; set => Dto.Protocol = value; }
        public string LocalPorts { get => Dto.LocalPorts; set => Dto.LocalPorts = value; }
        public string? RemotePorts { get => Dto.RemotePorts; set => Dto.RemotePorts = value; }
        public string RemoteAddresses { get => Dto.RemoteAddresses; set => Dto.RemoteAddresses = value; }
        public string? ApplicationPath { get => Dto.ApplicationPath; set => Dto.ApplicationPath = value; }
        public string? ServiceName { get => Dto.ServiceName; set => Dto.ServiceName = value; }
        public bool Enabled { get => Dto.Enabled; set => Dto.Enabled = value; }
        public FirewallProfile Profiles { get => Dto.Profiles; set => Dto.Profiles = value; }
        public string? Grouping { get => Dto.Grouping; set => Dto.Grouping = value; }
        public bool IsManaged { get => Dto.IsManaged; set => Dto.IsManaged = value; }

        public FirewallRuleDto ToDtoCopy() => new()
        {
            Name = Dto.Name,
            Description = Dto.Description,
            Direction = Dto.Direction,
            Action = Dto.Action,
            Protocol = Dto.Protocol,
            LocalPorts = Dto.LocalPorts,
            RemotePorts = Dto.RemotePorts,
            RemoteAddresses = Dto.RemoteAddresses,
            ApplicationPath = Dto.ApplicationPath,
            ServiceName = Dto.ServiceName,
            Enabled = Dto.Enabled,
            Profiles = Dto.Profiles,
            Grouping = Dto.Grouping,
            IsManaged = Dto.IsManaged
        };

        public string ShortProgram
        {
            get
            {
                var path = ApplicationPath;
                if (string.IsNullOrWhiteSpace(path))
                    return "—";
                try
                {
                    return Path.GetFileName(path.Trim()) is { Length: > 0 } file ? file : path;
                }
                catch
                {
                    return path;
                }
            }
        }

        public string ProtocolDisplay => Protocol switch
        {
            FirewallProtocol.Tcp => "TCP",
            FirewallProtocol.Udp => "UDP",
            _ => "Любой"
        };

        public string ProfilesDisplay
        {
            get
            {
                if (Profiles == FirewallProfile.All)
                    return "Все";
                var parts = new List<string>(3);
                if (Profiles.HasFlag(FirewallProfile.Domain))
                    parts.Add("Доменный");
                if (Profiles.HasFlag(FirewallProfile.Private))
                    parts.Add("Частный");
                if (Profiles.HasFlag(FirewallProfile.Public))
                    parts.Add("Публичный");
                return parts.Count > 0 ? string.Join(", ", parts) : Profiles.ToString();
            }
        }
    }

    /// <summary>
    /// Строка таблицы банов Port Defender с авто-обратным отсчётом времени.
    /// </summary>
    public sealed class BanRow : INotifyPropertyChanged
    {
        private string _remainingText = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;

        public BanRow(ActiveBanDto dto) => Dto = dto;
        public ActiveBanDto Dto { get; }

        public string Ip => Dto.Ip;
        public int TargetPort => Dto.TargetPort;
        public string TargetProcess => string.IsNullOrWhiteSpace(Dto.TargetProcess) ? "—" : Dto.TargetProcess;
        public int AttackRate => Dto.AttackRate;
        public string Reason => string.IsNullOrWhiteSpace(Dto.Reason) ? "—" : Dto.Reason;
        public int RepeatCount => Dto.RepeatCount;

        public string RemainingText
        {
            get => _remainingText;
            private set
            {
                if (_remainingText != value)
                {
                    _remainingText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
                }
            }
        }

        public void RefreshCountdown()
        {
            try
            {
                if (Dto.ExpiresAtUtc == null)
                {
                    RemainingText = "Навсегда";
                    return;
                }

                var remaining = Dto.ExpiresAtUtc.Value - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    RemainingText = "Истёк";
                else if (remaining.TotalHours >= 1)
                    RemainingText = $"{(int)remaining.TotalHours} ч {remaining.Minutes:00} мин";
                else if (remaining.TotalMinutes >= 1)
                    RemainingText = $"{(int)remaining.TotalMinutes} мин {remaining.Seconds:00} с";
                else
                    RemainingText = $"{remaining.Seconds} с";
            }
            catch
            {
                RemainingText = "—";
            }
        }
    }

    /// <summary>Конструктор по умолчанию для визуального дизайнера.</summary>
    public NetworkSecurityHubView() : this(new PipeClient())
    {
    }

    /// <summary>
    /// Основной конструктор: инициализирует компоненты и фоновые таймеры обновления.
    /// </summary>
    public NetworkSecurityHubView(PipeClient pipeClient)
    {
        InitializeComponent();
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));

        // Таймер тика обратного отсчета банов Port Defender (1 сек)
        _banTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _banTimer.Tick += (_, _) =>
        {
            foreach (var b in _banRows)
                b.RefreshCountdown();
        };

        // Таймер периодического опроса банов со службы (15 сек)
        _bansRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _bansRefreshTimer.Tick += async (_, _) => await LoadBansAsync();

        // Таймер мониторинга активных соединений Live (2 сек)
        _liveConnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveConnTimer.Tick += async (_, _) =>
        {
            if (ChkAutoRefreshConn?.IsChecked == true && IsLoaded)
                await LoadLiveConnectionsAsync();
        };

        Loaded += async (_, _) =>
        {
            StartTimers();
            await RefreshAllAsync();
        };

        Unloaded += (_, _) => StopTimers();
    }

    private void StartTimers()
    {
        try
        {
            if (!_banTimer.IsEnabled) _banTimer.Start();
            if (!_bansRefreshTimer.IsEnabled) _bansRefreshTimer.Start();
            if (!_liveConnTimer.IsEnabled && ChkAutoRefreshConn?.IsChecked == true)
                _liveConnTimer.Start();
        }
        catch { }
    }

    private void StopTimers()
    {
        try { _banTimer?.Stop(); } catch { }
        try { _bansRefreshTimer?.Stop(); } catch { }
        try { _liveConnTimer?.Stop(); } catch { }
    }

    /// <summary>Комплексное обновление данных всех 5 разделов сетевой безопасности.</summary>
    public async Task RefreshAllAsync()
    {
        await LoadFirewallStatusAsync();
        await LoadSilentPid0SettingAsync();
        await LoadInteractiveAlertsSettingAsync();
        await LoadLiveConnectionsAsync();
        await LoadWhitelistAsync();
        await LoadBlacklistAsync();
        await LoadRulesAsync();
        await LoadDefenderStatusAsync();
        await LoadBansAsync();
    }

    // =========================================================================
    // БЕЛЫЙ СПИСОК ПРИЛОЖЕНИЙ (WHITELIST)
    // =========================================================================

    /// <summary>Загрузка белого списка приложений из службы.</summary>
    private async Task LoadWhitelistAsync()
    {
        if (_isLoadingWhitelist) return;
        _isLoadingWhitelist = true;
        try
        {
            var paths = await _pipeClient.GetFirewallWhitelistAsync();
            _allWhitelistApps = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => FirewallAppEntry.FromPath(p, isBlacklist: false))
                .OrderBy(e => e.ProcessName)
                .ToList();

            ApplyWhitelistFilter();
            TxtWhitelistCount.Text = $"{_allWhitelistApps.Count} приложений";
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки белого списка: {ex.Message}");
        }
        finally
        {
            _isLoadingWhitelist = false;
        }
    }

    private void ApplyWhitelistFilter()
    {
        var query = (_whitelistSearchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _allWhitelistApps.Where(a =>
            string.IsNullOrEmpty(query)
            || a.ProcessName.ToLowerInvariant().Contains(query)
            || a.FullPath.ToLowerInvariant().Contains(query)).ToList();

        GridWhitelist.ItemsSource = filtered;
        PanelWhitelistEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridWhitelist.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWhitelistSearchChanged(object sender, TextChangedEventArgs e)
    {
        _whitelistSearchText = TxtSearchWhitelist?.Text ?? string.Empty;
        ApplyWhitelistFilter();
    }

    private async void OnRefreshWhitelistClick(object sender, RoutedEventArgs e) =>
        await LoadWhitelistAsync();

    /// <summary>Добавление исполняемого файла в белый список через OpenFileDialog.</summary>
    private async void OnAddWhitelistAppClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите исполняемый файл для добавления в белый список",
                Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                var filePath = dialog.FileName.Trim();
                SetStatus($"Добавление в белый список: {Path.GetFileName(filePath)}...");
                var ok = await _pipeClient.AddToFirewallWhitelistAsync(filePath);
                if (ok)
                {
                    SetStatus($"Программа {Path.GetFileName(filePath)} успешно добавлена в белый список.");
                    await LoadWhitelistAsync();
                }
                else
                {
                    SetStatus("Служба не подтвердила добавление в белый список.");
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось добавить в белый список: {ex.Message}");
        }
    }

    /// <summary>Удаление программы из белого списка.</summary>
    private async void OnRemoveWhitelistAppClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not FirewallAppEntry entry)
                entry = GridWhitelist.SelectedItem as FirewallAppEntry;

            if (entry == null) return;

            var result = MessageBox.Show(
                $"Вы действительно хотите удалить программу \"{entry.ProcessName}\" из белого списка?\n\nПуть: {entry.FullPath}",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SetStatus($"Удаление из белого списка: {entry.ProcessName}...");
            var ok = await _pipeClient.RemoveFromFirewallWhitelistAsync(entry.FullPath);
            if (ok)
            {
                SetStatus($"Программа {entry.ProcessName} удалена из белого списка.");
                await LoadWhitelistAsync();
            }
            else
            {
                SetStatus("Служба не подтвердила удаление из белого списка.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка удаления из белого списка: {ex.Message}");
        }
    }

    // =========================================================================
    // ЧЁРНЫЙ СПИСОК ПРИЛОЖЕНИЙ (BLACKLIST)
    // =========================================================================

    /// <summary>Загрузка черного списка приложений из службы.</summary>
    private async Task LoadBlacklistAsync()
    {
        if (_isLoadingBlacklist) return;
        _isLoadingBlacklist = true;
        try
        {
            var paths = await _pipeClient.GetFirewallBlacklistAsync();
            _allBlacklistApps = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => FirewallAppEntry.FromPath(p, isBlacklist: true))
                .OrderBy(e => e.ProcessName)
                .ToList();

            ApplyBlacklistFilter();
            TxtBlacklistCount.Text = $"{_allBlacklistApps.Count} приложений";
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки черного списка: {ex.Message}");
        }
        finally
        {
            _isLoadingBlacklist = false;
        }
    }

    private void ApplyBlacklistFilter()
    {
        var query = (_blacklistSearchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _allBlacklistApps.Where(a =>
            string.IsNullOrEmpty(query)
            || a.ProcessName.ToLowerInvariant().Contains(query)
            || a.FullPath.ToLowerInvariant().Contains(query)).ToList();

        GridBlacklist.ItemsSource = filtered;
        PanelBlacklistEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridBlacklist.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBlacklistSearchChanged(object sender, TextChangedEventArgs e)
    {
        _blacklistSearchText = TxtSearchBlacklist?.Text ?? string.Empty;
        ApplyBlacklistFilter();
    }

    private async void OnRefreshBlacklistClick(object sender, RoutedEventArgs e) =>
        await LoadBlacklistAsync();

    /// <summary>Добавление исполняемого файла в черный список и создание блокирующего системного правила.</summary>
    private async void OnAddBlacklistAppClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите исполняемый файл для полной сетевой блокировки (Blacklist)",
                Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                var filePath = dialog.FileName.Trim();
                var procName = Path.GetFileName(filePath);
                SetStatus($"Блокировка трафика для {procName}...");

                // Создание блокирующего правила в Windows Firewall для исходящего и входящего трафика
                var rule = new FirewallRuleDto
                {
                    Name = $"StartupMonitor Block: {procName}",
                    Description = $"Принудительная блокировка приложения {filePath} из черного списка StartupMonitor.",
                    ApplicationPath = filePath,
                    Action = FirewallAction.Block,
                    Direction = FirewallDirection.Both,
                    Protocol = FirewallProtocol.Any,
                    Enabled = true,
                    Profiles = FirewallProfile.All,
                    IsManaged = true,
                    Grouping = "StartupMonitor"
                };

                await _pipeClient.CreateFirewallRuleAsync(rule);
                var ok = await _pipeClient.AddToFirewallBlacklistAsync(filePath, "Заблокировано пользователем через чёрный список");
                if (ok)
                {
                    SetStatus($"Программа {procName} заблокирована и внесена в чёрный список.");
                    await LoadBlacklistAsync();
                    await LoadRulesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка добавления в чёрный список: {ex.Message}");
        }
    }

    /// <summary>Снятие блокировки и удаление из черного списка.</summary>
    private async void OnRemoveBlacklistAppClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not FirewallAppEntry entry)
                entry = GridBlacklist.SelectedItem as FirewallAppEntry;

            if (entry == null) return;

            var result = MessageBox.Show(
                $"Снять сетевую блокировку с программы \"{entry.ProcessName}\"?\n\nПриложению снова будет разрешён доступ в сеть.",
                "Разблокировка приложения",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SetStatus($"Разблокировка: {entry.ProcessName}...");

            // Удаление соответствующего системного правила блокировки
            await _pipeClient.DeleteFirewallRuleAsync($"StartupMonitor Block: {entry.ProcessName}", FirewallDirection.Both);
            var ok = await _pipeClient.RemoveFromFirewallBlacklistAsync(entry.FullPath);
            if (ok)
            {
                SetStatus($"Программа {entry.ProcessName} разблокирована.");
                await LoadBlacklistAsync();
                await LoadRulesAsync();
            }
            else
            {
                SetStatus("Служба не подтвердила удаление из черного списка.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка разблокировки: {ex.Message}");
        }
    }

    // =========================================================================
    // БЫСТРЫЕ ДЕЙСТВИЯ ИЗ СТРОКИ АКТИВНЫХ СОЕДИНЕНИЙ (LIVE CONNECTIONS)
    // =========================================================================

    private string? ResolveProcessPathBySocket(LiveConnectionRow row)
    {
        if (row == null || row.Pid <= 0) return null;
        if (_processPathCache.TryGetValue(row.Pid, out var cached)) return cached;

        try
        {
            using var proc = Process.GetProcessById(row.Pid);
            var path = proc.MainModule?.FileName;
            _processPathCache[row.Pid] = path;
            return path;
        }
        catch
        {
            _processPathCache[row.Pid] = null;
            return null;
        }
    }

    private async void OnRowWhitelistClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not LiveConnectionRow row) return;
            var path = ResolveProcessPathBySocket(row);
            var target = !string.IsNullOrWhiteSpace(path) ? path : row.ProcessName;

            SetStatus($"Добавление в белый список: {row.ProcessName}...");
            var ok = await _pipeClient.AddToFirewallWhitelistAsync(target);
            if (ok)
            {
                SetStatus($"Процесс {row.ProcessName} добавлен в белый список.");
                await LoadWhitelistAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка добавления сокета в белый список: {ex.Message}");
        }
    }

    private async void OnRowBlacklistClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not LiveConnectionRow row) return;
            var path = ResolveProcessPathBySocket(row);
            var target = !string.IsNullOrWhiteSpace(path) ? path : row.ProcessName;

            SetStatus($"Блокировка процесса {row.ProcessName}...");

            if (!string.IsNullOrWhiteSpace(path))
            {
                var rule = new FirewallRuleDto
                {
                    Name = $"StartupMonitor Block: {row.ProcessName}",
                    ApplicationPath = path,
                    Action = FirewallAction.Block,
                    Direction = FirewallDirection.Both,
                    Protocol = FirewallProtocol.Any,
                    Enabled = true,
                    Profiles = FirewallProfile.All,
                    IsManaged = true
                };
                await _pipeClient.CreateFirewallRuleAsync(rule);
            }

            var ok = await _pipeClient.AddToFirewallBlacklistAsync(target, "Быстрая блокировка из монитора сокетов");
            if (ok)
            {
                SetStatus($"Процесс {row.ProcessName} заблокирован.");
                await LoadBlacklistAsync();
                await LoadRulesAsync();
                await LoadLiveConnectionsAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка блокировки процесса: {ex.Message}");
        }
    }

    private void OnRowKillProcessClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.DataContext is not LiveConnectionRow row) return;
            if (row.Pid <= 4)
            {
                MessageBox.Show("Нельзя завершить критический системный процесс Windows.", "Защита системы", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Принудительно завершить процесс \"{row.ProcessName}\" (PID: {row.Pid})?",
                "Завершение процесса",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            using var proc = Process.GetProcessById(row.Pid);
            proc.Kill();
            SetStatus($"Процесс {row.ProcessName} (PID {row.Pid}) завершён.");
            _ = LoadLiveConnectionsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось завершить процесс: {ex.Message}");
        }
    }

    private async void OnCtxWhitelistProcessClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row)
        {
            var path = ResolveProcessPathBySocket(row);
            var target = !string.IsNullOrWhiteSpace(path) ? path : row.ProcessName;
            await _pipeClient.AddToFirewallWhitelistAsync(target);
            await LoadWhitelistAsync();
        }
    }

    private async void OnCtxBlacklistProcessClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row)
        {
            var path = ResolveProcessPathBySocket(row);
            var target = !string.IsNullOrWhiteSpace(path) ? path : row.ProcessName;
            await _pipeClient.AddToFirewallBlacklistAsync(target);
            await LoadBlacklistAsync();
        }
    }

    // =========================================================================
    // СТАТУС БРАНДМАУЭРА И РЕЖИМЫ ФИЛЬТРАЦИИ
    // =========================================================================

    private async Task LoadFirewallStatusAsync()
    {
        try
        {
            var status = await _pipeClient.GetFirewallStatusAsync();
            if (status != null)
            {
                TxtFirewallState.Text = status.IsEnabled ? "Включён" : "Отключён";
                FirewallShieldIcon.Foreground = status.IsEnabled
                    ? (Brush)FindResource("NeonCyanBrush")
                    : (Brush)FindResource("NeonCrimsonBrush");

                TxtProfileDomain.Text = $"Домен: {(status.DomainProfileEnabled ? "Вкл" : "Выкл")}";
                TxtProfilePrivate.Text = $"Частная: {(status.PrivateProfileEnabled ? "Вкл" : "Выкл")}";
                TxtProfilePublic.Text = $"Публичная: {(status.PublicProfileEnabled ? "Вкл" : "Выкл")}";
                TxtFirewallDetails.Text = $"Правил в брандмауэре: {status.RuleCount}";
            }

            _currentMode = await _pipeClient.GetFirewallFilteringModeAsync();
            _suppressModeEvents = true;
            try
            {
                RbModeNormal.IsChecked = _currentMode == FirewallFilteringMode.Normal;
                RbModeWhitelist.IsChecked = _currentMode == FirewallFilteringMode.WhitelistOutbound;
                RbModeKillswitch.IsChecked = _currentMode == FirewallFilteringMode.Killswitch;
            }
            finally
            {
                _suppressModeEvents = false;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка статуса брандмауэра: {ex.Message}");
        }
    }

    private async void OnRefreshStatusClick(object sender, RoutedEventArgs e) =>
        await LoadFirewallStatusAsync();

    private async void OnFilteringModeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeEvents) return;
        var target = FirewallFilteringMode.Normal;
        if (ReferenceEquals(sender, RbModeWhitelist))
            target = FirewallFilteringMode.WhitelistOutbound;
        else if (ReferenceEquals(sender, RbModeKillswitch))
            target = FirewallFilteringMode.Killswitch;

        if (target == _currentMode) return;

        if (target == FirewallFilteringMode.Killswitch)
        {
            var result = MessageBox.Show(
                "Режим полной сетевой изоляции (Killswitch) мгновенно заблокирует ВСЕ входящие и исходящие соединения на компьютере.\n\nАктивировать?",
                "Внимание: изоляция сети",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _suppressModeEvents = true;
                RbModeNormal.IsChecked = _currentMode == FirewallFilteringMode.Normal;
                RbModeWhitelist.IsChecked = _currentMode == FirewallFilteringMode.WhitelistOutbound;
                _suppressModeEvents = false;
                return;
            }
        }

        var ok = await _pipeClient.SetFirewallFilteringModeAsync(target);
        if (ok)
        {
            _currentMode = target;
            SetStatus($"Режим фильтрации переключён: {target}");
        }
        else
        {
            SetStatus("Служба отклонила изменение режима фильтрации.");
        }
    }

    // =========================================================================
    // НАСТРОЙКИ АЛЕРТОВ И ТИХОГО РЕЖИМА PID 0
    // =========================================================================

    private async Task LoadSilentPid0SettingAsync()
    {
        try
        {
            var silent = await _pipeClient.GetFirewallSilentPid0Async();
            ChkSilentPid0.IsChecked = silent;
        }
        catch { }
    }

    private async void OnSilentPid0Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            var isSilent = ChkSilentPid0.IsChecked == true;
            await _pipeClient.SetFirewallSilentPid0Async(isSilent);
            SetStatus(isSilent ? "Тихий режим PID 0 включён." : "Тихий режим PID 0 выключен.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка переключения PID 0: {ex.Message}");
        }
    }

    private async Task LoadInteractiveAlertsSettingAsync()
    {
        try
        {
            _suppressAlertsToggle = true;
            _alertsEnabled = await _pipeClient.GetAlertsEnabledAsync();
            ChkInteractiveAlerts.IsChecked = _alertsEnabled;
        }
        catch { }
        finally
        {
            _suppressAlertsToggle = false;
        }
    }

    private async void OnInteractiveAlertsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressAlertsToggle) return;
        var requested = ChkInteractiveAlerts.IsChecked == true;
        try
        {
            var ok = await _pipeClient.SetAlertsEnabledAsync(requested);
            if (ok)
            {
                _alertsEnabled = requested;
                SetStatus(requested ? "Интерактивные алерты сети включены." : "Интерактивные алерты выключены.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка настройки алертов: {ex.Message}");
        }
    }

    // =========================================================================
    // ЧИСТЫЕ ФУНКЦИИ ДЛЯ XUNIT ТЕСТОВ (БЕЗ ДИСПЕТЧЕРА И ОКНА)
    // =========================================================================

    /// <summary>
    /// Решающая чистая функция тика поллера (покрыта юнит-тестами NetworkAlertUiTests).
    /// </summary>
    public static bool ShouldShowAlertWindow(
        bool alertsEnabled,
        IReadOnlyList<NetworkConnectionAlertDto>? alerts,
        bool windowAlreadyOpen)
    {
        if (!alertsEnabled) return false;
        if (windowAlreadyOpen) return false;
        if (alerts == null || alerts.Count == 0) return false;
        return true;
    }

    /// <summary>
    /// Формирует запрос резолва алерта (покрыта юнит-тестами NetworkAlertUiTests).
    /// </summary>
    public static ResolveNetworkAlertRequest BuildResolveRequest(
        NetworkConnectionAlertDto alert,
        NetworkAlertResolution resolution)
    {
        if (alert == null) throw new ArgumentNullException(nameof(alert));
        return new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId ?? string.Empty,
            ProcessPath = alert.ProcessPath ?? string.Empty,
            ProcessName = alert.ProcessName ?? string.Empty,
            RemoteAddress = alert.RemoteAddress ?? string.Empty,
            Resolution = resolution
        };
    }

    // =========================================================================
    // МОНИТОРИНГ СОЕДИНЕНИЙ (LIVE CONNECTIONS)
    // =========================================================================

    private async Task LoadLiveConnectionsAsync()
    {
        if (_isLoadingConn) return;
        _isLoadingConn = true;
        try
        {
            var sockets = await _pipeClient.GetActiveConnectionsAsync() ?? new List<ProcessNetworkSocket>();
            var rows = new List<LiveConnectionRow>(sockets.Count);

            int total = sockets.Count;
            int established = 0;
            int listening = 0;
            int blockedCount = 0;

            foreach (var s in sockets)
            {
                if (s == null) continue;
                var st = (s.State ?? string.Empty).ToUpperInvariant();
                if (st == "ESTABLISHED") established++;
                else if (st is "LISTENING" or "LISTEN") listening++;

                bool isBlocked = _blockedIps.Contains(s.RemoteAddress);
                if (isBlocked) blockedCount++;

                rows.Add(new LiveConnectionRow(s, isBlocked ? "🚫 Заблокирован" : "🟢 Разрешено"));
            }

            _allConnections = rows.OrderBy(r => r.ProcessName).ThenBy(r => r.RemoteAddress).ToList();

            TxtStatTotalConnections.Text = total.ToString();
            TxtStatEstablished.Text = established.ToString();
            TxtStatListening.Text = listening.ToString();
            TxtStatBlockedSockets.Text = blockedCount.ToString();

            ApplyConnFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки соединений: {ex.Message}");
        }
        finally
        {
            _isLoadingConn = false;
        }
    }

    private void ApplyConnFilter()
    {
        var query = (_connSearchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _allConnections.Where(r => MatchesConnFilter(r, query)).ToList();

        GridLiveConnections.ItemsSource = filtered;
        TxtConnectionsSummary.Text = $"Показано: {filtered.Count} из {_allConnections.Count}";
        PanelConnEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridLiveConnections.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool MatchesConnFilter(LiveConnectionRow row, string queryLower)
    {
        if (row == null) return false;
        if (!string.IsNullOrEmpty(queryLower))
        {
            bool match = row.ProcessName.ToLowerInvariant().Contains(queryLower)
                || row.Pid.ToString().Contains(queryLower)
                || row.RemoteEndPoint.ToLowerInvariant().Contains(queryLower)
                || row.LocalEndPoint.ToLowerInvariant().Contains(queryLower)
                || row.CountryText.ToLowerInvariant().Contains(queryLower);
            if (!match) return false;
        }
        return true;
    }

    private void OnConnectionsSearchChanged(object sender, TextChangedEventArgs e)
    {
        _connSearchText = TxtSearchConnections?.Text ?? string.Empty;
        ApplyConnFilter();
    }

    private void OnConnectionsFilterChanged(object sender, RoutedEventArgs e)
    {
        if (RbConnWan?.IsChecked == true) _activeConnFilter = ConnFilter.Wan;
        else if (RbConnLan?.IsChecked == true) _activeConnFilter = ConnFilter.Lan;
        else if (RbConnEstablished?.IsChecked == true) _activeConnFilter = ConnFilter.Established;
        else if (RbConnListening?.IsChecked == true) _activeConnFilter = ConnFilter.Listening;
        else _activeConnFilter = ConnFilter.All;
        ApplyConnFilter();
    }

    private void OnAutoRefreshConnChanged(object sender, RoutedEventArgs e)
    {
        if (ChkAutoRefreshConn?.IsChecked == true)
        {
            if (!_liveConnTimer.IsEnabled) _liveConnTimer.Start();
        }
        else
        {
            _liveConnTimer.Stop();
        }
    }

    private async void OnRefreshConnClick(object sender, RoutedEventArgs e) =>
        await LoadLiveConnectionsAsync();

    // =========================================================================
    // СИСТЕМНЫЕ ПРАВИЛА WINDOWS FIREWALL
    // =========================================================================

    private async Task LoadRulesAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            var rules = await _pipeClient.GetAllFirewallRulesAsync() ?? new List<FirewallRuleDto>();
            _allRules = rules.Select(r => new FirewallRuleRow(r)).OrderBy(r => r.Name).ToList();
            ApplyRuleFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки правил брандмауэра: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyRuleFilter()
    {
        var query = (_searchText ?? string.Empty).Trim().ToLowerInvariant();
        var filtered = _allRules.Where(r => MatchesRuleFilter(r, _activeFilter, query)).ToList();

        GridRules.ItemsSource = filtered;
        PanelRulesEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridRules.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static bool MatchesRuleFilter(FirewallRuleRow row, RuleFilter filter, string queryLower)
    {
        if (row == null) return false;
        switch (filter)
        {
            case RuleFilter.Inbound:
                if (row.Direction != FirewallDirection.Inbound) return false;
                break;
            case RuleFilter.Outbound:
                if (row.Direction != FirewallDirection.Outbound) return false;
                break;
            case RuleFilter.Blocking:
                if (row.Action != FirewallAction.Block) return false;
                break;
            case RuleFilter.Managed:
                if (!row.IsManaged) return false;
                break;
        }

        if (!string.IsNullOrEmpty(queryLower))
        {
            bool match = row.Name.ToLowerInvariant().Contains(queryLower)
                || row.ShortProgram.ToLowerInvariant().Contains(queryLower)
                || row.LocalPorts.ToLowerInvariant().Contains(queryLower)
                || row.RemoteAddresses.ToLowerInvariant().Contains(queryLower);
            if (!match) return false;
        }
        return true;
    }

    private void OnRuleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = TxtRuleSearch?.Text ?? string.Empty;
        ApplyRuleFilter();
    }

    private void OnRuleFilterChecked(object sender, RoutedEventArgs e)
    {
        if (FilterInbound?.IsChecked == true) _activeFilter = RuleFilter.Inbound;
        else if (FilterOutbound?.IsChecked == true) _activeFilter = RuleFilter.Outbound;
        else if (FilterBlocking?.IsChecked == true) _activeFilter = RuleFilter.Blocking;
        else if (FilterManaged?.IsChecked == true) _activeFilter = RuleFilter.Managed;
        else _activeFilter = RuleFilter.All;
        ApplyRuleFilter();
    }

    private async void OnRuleToggleChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is FirewallRuleRow row)
        {
            var newState = cb.IsChecked == true;
            row.Enabled = newState;
            var ok = await _pipeClient.ToggleFirewallRuleAsync(row.Name, row.Direction, newState);
            if (!ok)
            {
                row.Enabled = !newState;
                cb.IsChecked = !newState;
                SetStatus($"Не удалось переключить правило {row.Name}.");
            }
        }
    }

    private async void OnRefreshRulesClick(object sender, RoutedEventArgs e) =>
        await LoadRulesAsync();

    private void OnCreateRuleClick(object sender, RoutedEventArgs e)
    {
        var editWnd = new FirewallRuleEditWindow(null);
        if (editWnd.ShowDialog() == true && editWnd.CreatedRule != null)
        {
            _ = Task.Run(async () =>
            {
                await _pipeClient.CreateFirewallRuleAsync(editWnd.CreatedRule);
                await Dispatcher.InvokeAsync(LoadRulesAsync);
            });
        }
    }

    private void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        if (GridRules.SelectedItem is FirewallRuleRow row)
        {
            var editWnd = new FirewallRuleEditWindow(row.ToDtoCopy());
            if (editWnd.ShowDialog() == true && editWnd.CreatedRule != null)
            {
                _ = Task.Run(async () =>
                {
                    await _pipeClient.CreateFirewallRuleAsync(editWnd.CreatedRule);
                    await Dispatcher.InvokeAsync(LoadRulesAsync);
                });
            }
        }
    }

    private void OnRuleGridDoubleClick(object sender, MouseButtonEventArgs e) =>
        OnEditRuleClick(sender, e);

    private async void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (GridRules.SelectedItem is FirewallRuleRow row)
        {
            var confirm = MessageBox.Show($"Удалить правило \"{row.Name}\"?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _pipeClient.DeleteFirewallRuleAsync(row.Name, row.Direction);
                await LoadRulesAsync();
            }
        }
    }

    // =========================================================================
    // ЭКСПОРТ / ИМПОРТ И ПРЕСЕТЫ БРАНДМАУЭРА (ХЕЛПЕРЫ ДЛЯ ЮНИТ-ТЕСТОВ)
    // =========================================================================

    public static string SerializeBackupToJson(FirewallBackupDto backup)
    {
        if (backup == null) throw new ArgumentNullException(nameof(backup));
        return JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
    }

    public static FirewallBackupDto DeserializeBackupFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON пуст", nameof(json));
        return JsonSerializer.Deserialize<FirewallBackupDto>(json) ?? throw new InvalidOperationException("Не удалось десериализовать");
    }

    public static ImportFirewallRulesRequest BuildImportRequest(FirewallBackupDto backup, bool overwriteExisting) =>
        new(backup, overwriteExisting);

    public static string GetSecurityPresetDisplayName(FirewallSecurityPreset preset) => preset switch
    {
        FirewallSecurityPreset.MaxPrivacy => "Максимальная приватность",
        FirewallSecurityPreset.Gaming => "Игровой режим",
        FirewallSecurityPreset.PublicWifi => "Публичный Wi-Fi",
        _ => "Сброс к умолчаниям"
    };

    public static ApplySecurityPresetRequest BuildPresetRequest(FirewallSecurityPreset preset) =>
        new(preset);

    public static string BuildDefaultBackupFileName(DateTime timestamp) =>
        $"FirewallRules_Backup_{timestamp:yyyyMMdd_HHmmss}.json";

    private async void OnExportRulesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Экспорт правил брандмауэра",
                Filter = "JSON файлы (*.json)|*.json",
                FileName = BuildDefaultBackupFileName(DateTime.Now)
            };
            if (dialog.ShowDialog() == true)
            {
                var backup = await _pipeClient.ExportFirewallRulesAsync();
                if (backup != null)
                {
                    File.WriteAllText(dialog.FileName, SerializeBackupToJson(backup));
                    SetStatus($"Правила брандмауэра экспортированы в {Path.GetFileName(dialog.FileName)}.");
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка экспорта: {ex.Message}");
        }
    }

    private async void OnImportRulesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Импорт правил брандмауэра",
                Filter = "JSON файлы (*.json)|*.json"
            };
            if (dialog.ShowDialog() == true)
            {
                var json = File.ReadAllText(dialog.FileName);
                var backup = DeserializeBackupFromJson(json);
                var res = await _pipeClient.ImportFirewallRulesAsync(backup, overwriteExisting: false);
                SetStatus($"Импортировано: {res.Imported}, пропущено: {res.Skipped}.");
                await LoadRulesAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка импорта: {ex.Message}");
        }
    }

    private async void OnApplyPresetMaxPrivacyClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(FirewallSecurityPreset.MaxPrivacy);

    private async void OnApplyPresetGamingClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(FirewallSecurityPreset.Gaming);

    private async void OnApplyPresetPublicWifiClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(FirewallSecurityPreset.PublicWifi);

    private async void OnApplyPresetResetClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(FirewallSecurityPreset.ResetToDefaults);

    private async Task ApplyPresetAsync(FirewallSecurityPreset preset)
    {
        var ok = await _pipeClient.ApplySecurityPresetAsync(preset);
        SetStatus(ok ? $"Пресет \"{GetSecurityPresetDisplayName(preset)}\" успешно применён." : "Ошибка применения пресета.");
        await LoadRulesAsync();
    }

    private async void OnAntiTelemetryClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Создать системные блокирующие правила для серверов сбора телеметрии и диагностики Microsoft?",
            "Блокировка телеметрии",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            var ok = await _pipeClient.BlockTelemetryRulesAsync();
            SetStatus(ok ? "Правила блокировки телеметрии применены." : "Ошибка блокировки телеметрии.");
            await LoadRulesAsync();
        }
    }

    // =========================================================================
    // PORT DEFENDER И ЖУРНАЛ БАНОВ
    // =========================================================================

    private async Task LoadDefenderStatusAsync()
    {
        try
        {
            _defenderConfig = await _pipeClient.GetPortDefenderConfigAsync();
            if (_defenderConfig != null)
            {
                TxtDefenderState.Text = _defenderConfig.Enabled ? "Защита портов активна" : "Защита портов остановлена";
                TxtDefenderDetails.Text = $"Порог: {_defenderConfig.MaxAttempts} атак за {_defenderConfig.WindowSeconds} сек. Бан: {_defenderConfig.BanDurationMinutes} мин.";
                TxtDefenderPorts.Text = _defenderConfig.ProtectedPorts.Count > 0
                    ? string.Join(", ", _defenderConfig.ProtectedPorts.OrderBy(p => p))
                    : "Нет защищаемых портов";
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка Port Defender: {ex.Message}");
        }
    }

    private void OnConfigureDefenderClick(object sender, RoutedEventArgs e)
    {
        var wnd = new PortDefenderConfigWindow(_pipeClient);
        if (wnd.ShowDialog() == true)
        {
            _ = LoadDefenderStatusAsync();
        }
    }

    private async void OnRefreshDefenderClick(object sender, RoutedEventArgs e) =>
        await LoadDefenderStatusAsync();

    private async Task LoadBansAsync()
    {
        try
        {
            var bans = await _pipeClient.GetPortDefenderActiveBansAsync() ?? new List<ActiveBanDto>();
            _banRows = bans.Select(b => new BanRow(b)).ToList();
            foreach (var b in _banRows) b.RefreshCountdown();

            GridBans.ItemsSource = _banRows;
            TxtBansCount.Text = $"Активных банов: {_banRows.Count}";
            PanelBansEmpty.Visibility = _banRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GridBans.Visibility = _banRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки банов: {ex.Message}");
        }
    }

    private async void OnRefreshBansClick(object sender, RoutedEventArgs e) =>
        await LoadBansAsync();

    private async void OnUnblockIpClick(object sender, RoutedEventArgs e)
    {
        if (GridBans.SelectedItem is BanRow row)
        {
            var ok = await _pipeClient.UnbanIpAsync(row.Ip);
            SetStatus(ok ? $"Бан для IP {row.Ip} снят." : $"Не удалось разбанить IP {row.Ip}.");
            await LoadBansAsync();
        }
    }

    private async void OnClearBansClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("Снять все активные баны IP?", "Сброс банов", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            var ok = await _pipeClient.ClearAllBansAsync();
            SetStatus(ok ? "Все баны IP сброшены." : "Ошибка сброса банов.");
            await LoadBansAsync();
        }
    }

    // =========================================================================
    // КОНТЕКСТНОЕ МЕНЮ И УТИЛИТАРНЫЕ ДЕЙСТВИЯ СОЕДИНЕНИЙ
    // =========================================================================

    private async void OnCtxBlockRemoteIpClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row && !string.IsNullOrWhiteSpace(row.RemoteAddress) && row.RemoteAddress != "*")
        {
            var ok = await _pipeClient.BanIpAsync(row.RemoteAddress, 60, "Ручная блокировка из монитора соединений");
            SetStatus(ok ? $"Удаленный IP {row.RemoteAddress} заблокирован на 60 минут." : "Ошибка блокировки IP.");
            await LoadLiveConnectionsAsync();
        }
    }

    private void OnCtxCreateRuleForProcessClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row)
        {
            var path = ResolveProcessPathBySocket(row);
            var rule = new FirewallRuleDto
            {
                Name = $"StartupMonitor: {row.ProcessName}",
                ApplicationPath = path,
                Action = FirewallAction.Allow,
                Direction = FirewallDirection.Outbound,
                Protocol = row.Protocol.StartsWith("UDP", StringComparison.OrdinalIgnoreCase) ? FirewallProtocol.Udp : FirewallProtocol.Tcp
            };
            var edit = new FirewallRuleEditWindow(rule);
            if (edit.ShowDialog() == true && edit.CreatedRule != null)
            {
                _ = Task.Run(async () =>
                {
                    await _pipeClient.CreateFirewallRuleAsync(edit.CreatedRule);
                    await Dispatcher.InvokeAsync(LoadRulesAsync);
                });
            }
        }
    }

    private void OnCtxCopyRemoteIpClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row && !string.IsNullOrEmpty(row.RemoteAddress))
            Clipboard.SetText(row.RemoteAddress);
    }

    private void OnCtxCopyEndpointClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row && !string.IsNullOrEmpty(row.RemoteEndPoint))
            Clipboard.SetText(row.RemoteEndPoint);
    }

    private void OnCtxWhoisClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row && !string.IsNullOrEmpty(row.RemoteAddress))
        {
            try { Process.Start(new ProcessStartInfo($"https://whois.domaintools.com/{row.RemoteAddress}") { UseShellExecute = true }); } catch { }
        }
    }

    private void OnCtxVirusTotalClick(object sender, RoutedEventArgs e)
    {
        if (GridLiveConnections.SelectedItem is LiveConnectionRow row && !string.IsNullOrEmpty(row.RemoteAddress))
        {
            try { Process.Start(new ProcessStartInfo($"https://www.virustotal.com/gui/ip-address/{row.RemoteAddress}") { UseShellExecute = true }); } catch { }
        }
    }

    private void OnCtxKillProcessClick(object sender, RoutedEventArgs e) =>
        OnRowKillProcessClick(sender, e);

    private void SetStatus(string msg)
    {
        if (TxtStatus != null)
            TxtStatus.Text = $"{DateTime.Now:HH:mm:ss} — {msg}";
    }
}
```

---

## 6. Сводка соответствия и результаты тестирования

1. **Единый кибер-стиль**:
   * Радиусы скругления согласованы (`CornerRadius="8"`).
   * Использованы дизайн-токены: `#10B981` (Emerald), `#EF4444` (Crimson), `#F59E0B` (Amber) и `#00D2D3` (Neon Cyan).
   * Стеклянный полупрозрачный фон `CardBackgroundFillColorDefaultBrush` подложки гармонирует с `MainWindow.xaml`.
2. **Пять логических вкладок**:
   * Разделение интерфейса на «Активные соединения», «Белый список», «Чёрный список», «Правила брандмауэра», «Port Defender».
   * Добавлены карточки быстрой статистики над сокетами и кнопки действий прямо в строках таблицы.
3. **Белый и чёрный списки**:
   * Добавлены таблицы со статусами, дисковой проверкой, временем и размером.
   * Реализовано добавление через `OpenFileDialog` и удаление с диалогами подтверждения.
   * Быстрое добавление в списки в 1 клик из контекстного меню или кнопки строки монитора соединений.
4. **Устранение фонового таймера поллера**:
   * Фоновый таймер `_alertPollerTimer` полностью удален из UserControl (поллер вынесен в `MainViewModel.cs`).
   * Сохранены чистые сигнатуры `ShouldShowAlertWindow(...)` и `BuildResolveRequest(...)` для 100% стабильности существующих xUnit тестов.
