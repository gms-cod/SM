# SWARM_BEAM_SNAPSHOTS

## ???? ?????????

???????? ??????? ???????? ???????? ?? ?????????? `SnapshotsWindow` ?? ?????????? ??????? `MainWindow`.

- ?????????? ????????? ??? ????????: **9** (`Tag="9"`).
- ????? ???????? ???? ???????? ????? ????? ?????????????? (`NavItem0`, Tag 0).
- ????? ?????????? ??????? `SavedSnapshotsCount` ????????.
- ???????????? ??????????? ????? `NavigateToPage(9)`.

## 1. ??????? ???? (`MainWindow.xaml`)

```xaml
<Border x:Name="NavItemSnapshots" Tag="9" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
    <Grid>
        <Rectangle x:Name="NavBarSnapshots" Width="3.5" Height="22" HorizontalAlignment="Left" RadiusX="1.75" RadiusY="1.75"
                   Fill="{StaticResource NavActiveMarkerBrush}" Visibility="Collapsed" />
        <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
            <Viewbox Width="16" Height="16">
                <Path Data="{StaticResource IconCamera}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
            </Viewbox>
            <TextBlock Text="{Binding NavSnapshots, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
            <Border Margin="8 0 0 0" Style="{DynamicResource BadgeBluePill}">
                <TextBlock Text="{Binding SavedSnapshotsCount}" FontSize="10.5" FontWeight="700"
                           Foreground="{DynamicResource BadgeBlueTextBrush}" />
            </Border>
        </StackPanel>
    </Grid>
</Border>
```

## 2. ?????????? ???????? (`SnapshotsPage`)

```xaml
<Grid Grid.Column="1" x:Name="SnapshotsPage" Tag="9" Visibility="Collapsed">
<Grid Margin="16,0,16,16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- TitleBar -->

        <!-- Блок создания нового снимка -->
        <ui:Card Grid.Row="1" Margin="0,4,0,12" Padding="14">
            <StackPanel>
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Path Grid.Column="0" Data="{StaticResource IconCamera}" Width="18" Height="18"
                          Fill="{ui:ThemeResource AccentTextFillColorPrimaryBrush}" Stretch="Uniform" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <TextBlock Grid.Column="1" Text="{Binding SnapshotsSubtitle, Source={StaticResource Loc}}"
                               FontSize="13" Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" VerticalAlignment="Center" />
                </Grid>

                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="2*" />
                        <ColumnDefinition Width="3*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <ui:TextBox x:Name="TxtSnapshotName" Grid.Column="0" Margin="0,0,8,0"
                                PlaceholderText="{Binding SnapshotNamePrompt, Source={StaticResource Loc}}" />
                    <ui:TextBox x:Name="TxtSnapshotDesc" Grid.Column="1" Margin="0,0,8,0"
                                PlaceholderText="{Binding SnapshotDescPrompt, Source={StaticResource Loc}}" />
                    <ui:Button x:Name="BtnCreateSnapshot" Grid.Column="2" Appearance="Primary"
                               Content="{Binding CreateSnapshotBtn, Source={StaticResource Loc}}"
                               Click="OnCreateSnapshotClick" />
                </Grid>
            </StackPanel>
        </ui:Card>

        <!-- Основная область: Список снимков и результаты сравнения -->
        <Grid Grid.Row="2">
            <Grid.RowDefinitions>
                <RowDefinition Height="200" MinHeight="120" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" MinHeight="160" />
            </Grid.RowDefinitions>

            <!-- Верхняя часть: Таблица сохраненных снимков -->
            <ui:Card Grid.Row="0" Margin="0,0,0,4" Padding="8">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <TextBlock Grid.Row="0" Text="{Binding SnapSavedSnapshots, Source={StaticResource Loc}}" FontWeight="SemiBold" Margin="4,0,0,6" FontSize="13"
                               Foreground="{ui:ThemeResource TextFillColorPrimaryBrush}" />

                    <DataGrid x:Name="GridSnapshots" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False"
                              IsReadOnly="True" SelectionMode="Single" HeadersVisibility="Column"
                              GridLinesVisibility="Horizontal" BorderThickness="0" Background="Transparent">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="{Binding SnapColName, Source={StaticResource Loc}}" Binding="{Binding Name}" Width="2*" />
                            <DataGridTextColumn Header="{Binding SnapColDate, Source={StaticResource Loc}}" Binding="{Binding CreatedAt, StringFormat=\{0:dd.MM.yyyy HH:mm\}}" Width="130" />
                            <DataGridTextColumn Header="{Binding SnapColEntries, Source={StaticResource Loc}}" Binding="{Binding EntriesCount}" Width="80" />
                            <DataGridTextColumn Header="{Binding SnapColMachine, Source={StaticResource Loc}}" Binding="{Binding MachineName}" Width="1.5*" />
                            <DataGridTextColumn Header="{Binding SnapColDesc, Source={StaticResource Loc}}" Binding="{Binding Description}" Width="2*" />
                            <DataGridTemplateColumn Width="220" Header="{Binding ColumnActions, Source={StaticResource Loc}}">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                            <ui:Button Appearance="Primary" Padding="8,4" Margin="0,0,6,0"
                                                       Content="{Binding SnapBtnCompare, Source={StaticResource Loc}}" Click="OnCompareSnapshotClick"
                                                       ToolTip="{Binding SnapCompareTooltip, Source={StaticResource Loc}}" />
                                            <ui:Button Appearance="Danger" Padding="8,4"
                                                       Content="🗑" Click="OnDeleteSnapshotClick"
                                                       ToolTip="{Binding SnapDeleteTooltip, Source={StaticResource Loc}}" />
                                        </StackPanel>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
                        </DataGrid.Columns>
                    </DataGrid>

                    <!-- Пустое состояние -->
                    <TextBlock x:Name="TxtNoSnapshots" Grid.Row="1" Visibility="Collapsed"
                               Text="{Binding SnapshotNoSnapshots, Source={StaticResource Loc}}"
                               TextWrapping="Wrap" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="20"
                               Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}" TextAlignment="Center" />
                </Grid>
            </ui:Card>

            <!-- Разделитель областей снимков и анализа -->
            <GridSplitter Grid.Row="1" Height="6" HorizontalAlignment="Stretch" VerticalAlignment="Center" Background="Transparent" />

            <!-- Нижняя часть: Результаты сравнительного анализа («Было — Стало») -->
            <ui:Card Grid.Row="2" Margin="0,4,0,0" Padding="12">
                <Grid x:Name="PanelDiff">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>

                    <!-- Панель KPI и кнопки применения -->
                    <Grid Grid.Row="0" Margin="0,0,0,8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <!-- Счетчики изменений -->
                        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock x:Name="TxtDiffHeader" Text="{Binding SnapResultsHeader, Source={StaticResource Loc}}" FontWeight="Bold" FontSize="14" VerticalAlignment="Center" Margin="0,0,14,0" />

                            <Border Background="#10B981" CornerRadius="12" Padding="8,3" Margin="0,0,8,0">
                                <TextBlock x:Name="BadgeAdded" Text="+0 Новых" Foreground="White" FontWeight="Bold" FontSize="11" />
                            </Border>
                            <Border Background="#3B82F6" CornerRadius="12" Padding="8,3" Margin="0,0,8,0">
                                <TextBlock x:Name="BadgeModified" Text="~0 Измененных" Foreground="White" FontWeight="Bold" FontSize="11" />
                            </Border>
                            <Border Background="#EF4444" CornerRadius="12" Padding="8,3" Margin="0,0,8,0">
                                <TextBlock x:Name="BadgeRemoved" Text="-0 Удаленных" Foreground="White" FontWeight="Bold" FontSize="11" />
                            </Border>
                            <Border Background="#6B7280" CornerRadius="12" Padding="8,3">
                                <TextBlock x:Name="BadgeUnchanged" Text="0 Без изменений" Foreground="White" FontWeight="SemiBold" FontSize="11" />
                            </Border>
                        </StackPanel>

                        <!-- Кнопка фильтрации в главном окне -->
                        <ui:Button x:Name="BtnApplyFilter" Grid.Column="1" Appearance="Primary"
                                   Content="{Binding SnapshotApplyFilter, Source={StaticResource Loc}}"
                                   Click="OnApplyFilterToMainClick" />
                    </Grid>

                    <!-- Вкладки с деталями различий -->
                    <TabControl x:Name="TabsDiff" Grid.Row="1" Background="Transparent" BorderThickness="0">
                        <!-- Вкладка: Новые записи -->
                        <TabItem x:Name="TabAdded" Header="Новые записи (+0)">
                            <DataGrid x:Name="GridAdded" AutoGenerateColumns="False" CanUserAddRows="False"
                                      IsReadOnly="True" HeadersVisibility="Column" Background="Transparent" BorderThickness="0">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="{Binding ColumnName, Source={StaticResource Loc}}" Binding="{Binding Name}" Width="180" />
                                    <DataGridTextColumn Header="{Binding SnapColLocation, Source={StaticResource Loc}}" Binding="{Binding Location}" Width="120" />
                                    <DataGridTextColumn Header="{Binding SnapColCommandLine, Source={StaticResource Loc}}" Binding="{Binding DisplayPath}" Width="*" />
                                    <DataGridTextColumn Header="{Binding ColumnPublisher, Source={StaticResource Loc}}" Binding="{Binding Publisher}" Width="150" />
                                </DataGrid.Columns>
                            </DataGrid>
                        </TabItem>

                        <!-- Вкладка: Измененные записи -->
                        <TabItem x:Name="TabModified" Header="Измененные (~0)">
                            <DataGrid x:Name="GridModified" AutoGenerateColumns="False" CanUserAddRows="False"
                                      IsReadOnly="True" HeadersVisibility="Column" Background="Transparent" BorderThickness="0">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="{Binding ColumnName, Source={StaticResource Loc}}" Binding="{Binding CurrentEntry.Name}" Width="160" />
                                    <DataGridTextColumn Header="{Binding SnapColChangedParams, Source={StaticResource Loc}}" Binding="{Binding Summary}" Width="180" />
                                    <DataGridTextColumn Header="{Binding SnapColCurrentState, Source={StaticResource Loc}}" Binding="{Binding CurrentEntry.DisplayPath}" Width="2*" />
                                    <DataGridTextColumn Header="{Binding SnapColBaselineState, Source={StaticResource Loc}}" Binding="{Binding BaselineEntry.CommandLine}" Width="2*" />
                                </DataGrid.Columns>
                            </DataGrid>
                        </TabItem>

                        <!-- Вкладка: Удаленные записи -->
                        <TabItem x:Name="TabRemoved" Header="Удаленные (-0)">
                            <DataGrid x:Name="GridRemoved" AutoGenerateColumns="False" CanUserAddRows="False"
                                      IsReadOnly="True" HeadersVisibility="Column" Background="Transparent" BorderThickness="0">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="{Binding ColumnName, Source={StaticResource Loc}}" Binding="{Binding Name}" Width="180" />
                                    <DataGridTextColumn Header="{Binding SnapColLocation, Source={StaticResource Loc}}" Binding="{Binding Location}" Width="120" />
                                    <DataGridTextColumn Header="{Binding SnapColCommandLine, Source={StaticResource Loc}}" Binding="{Binding CommandLine}" Width="*" />
                                    <DataGridTextColumn Header="{Binding ColumnPublisher, Source={StaticResource Loc}}" Binding="{Binding Publisher}" Width="150" />
                                </DataGrid.Columns>
                            </DataGrid>
                        </TabItem>
                    </TabControl>
                </Grid>
            </ui:Card>
        </Grid>

        <!-- Подвал с кнопкой закрытия -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <ui:Button Appearance="Secondary" Content="{Binding SnapBtnClose, Source={StaticResource Loc}}" Width="110" Click="OnCloseClick" />
        </StackPanel>
    </Grid>
</Grid>
```

## 3. `NavigateToPage` (`MainWindow.xaml.cs`)

```csharp
SnapshotsPage.Visibility = idx == 9 ? Visibility.Visible : Visibility.Collapsed;

var items = new[] { NavItem0, NavItem1, NavItem2, NavItem3, NavItem4, NavItem5, NavItem6, NavItem7, NavItem8, NavItemSnapshots };
var bars = new[] { NavBar0, NavBar1, NavBar2, NavBar3, NavBar4, NavBar5, NavBar6, NavBar7, NavBar8, NavBarSnapshots };

// ...
else if (idx == 9)
    _ = LoadSnapshotsAsync();
```

## 4. ???????? ? ???????????

```csharp
private async Task LoadSnapshotsAsync()
{
    try
    {
        var snapshots = await _pipeClient.GetSnapshotsAsync();
        GridSnapshots.ItemsSource = snapshots;
        TxtNoSnapshots.Visibility = (snapshots == null || snapshots.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.SavedSnapshotsCount = snapshots?.Count ?? 0;
    }
    catch (Exception ex)
    {
        MessageBox.Show($"?????? ???????? ???????: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
```

??????????? `OnCreateSnapshotClick`, `OnCompareSnapshotClick`, `OnDeleteSnapshotClick`,
`OnApplyFilterToMainClick`, `OnCloseClick` ?????????? ?? `SnapshotsWindow.xaml.cs`.
`OnApplyFilterToMainClick` ????????? ????????? ? ?????????? ?? ???????? ?????? (`NavigateToPage(0)`),
`OnCloseClick` ????? ?????????? ?? ???????? ??????.
