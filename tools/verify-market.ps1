$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework
[Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path build/OrbitCalendar.exe))
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$watch = $assembly.GetType('OrbitCalendar.Watchlist')
$catalog = $assembly.GetType('OrbitCalendar.Instruments')
$folder = Join-Path $env:TEMP ('orbit-market-qa-' + [guid]::NewGuid())
New-Item -ItemType Directory $folder | Out-Null
$watch.GetField('path', $static).SetValue($null, (Join-Path $folder 'watchlist.json'))
$watch.GetMethod('Reload', $static).Invoke($null, @()) | Out-Null
$prefs=$assembly.GetType('OrbitCalendar.Preferences');$prefs.GetField('path',$static).SetValue($null,(Join-Path $folder 'display.txt'));$prefs.GetField('LastTab',$static).SetValue($null,0)
$items = $watch.GetField('Items', $static).GetValue($null)
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Search($query, [int]$filter = 0) { $catalog.GetMethod('Search', $static).Invoke($null, [object[]]@($query,$filter)) }
function Pump([int]$milliseconds) {
    $frame = [Windows.Threading.DispatcherFrame]::new()
    $timer = [Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds($milliseconds)
    $timer.Add_Tick({ $timer.Stop(); $frame.Continue = $false }.GetNewClosure())
    $timer.Start(); [Windows.Threading.Dispatcher]::PushFrame($frame)
}
function WaitUntil([scriptblock]$condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    while (-not (& $condition)) { if ([DateTime]::UtcNow -gt $deadline) { throw 'Async operation timed out' }; Pump 100 }
    Pump 1800
}
Assert ($items.Count -eq 2 -and $items[0].Id -eq 'y:^N225' -and $items[1].Id -eq 'y:JPY=X') 'Incorrect defaults'
foreach ($query in @('オルカン','おるかん','ｵﾙｶﾝ','0331418a','０３３１４１８Ａ','JP90C000H1T1')) {
    $result = @(Search $query 1)
    Assert ($result.Count -gt 0 -and $result[0].Id -eq 'f:0331418A') ('Alias/code search failed: ' + $query)
}
Assert (@(Search 'ＳＢＩ　Ｓ＆Ｐ５００' 1).Count -gt 0) 'Multi-term fund search failed'
Assert (@(Search '7203.T' 2)[0].Code -eq '7203') 'Stock ticker search failed'
Assert (@(Search 'USD/JPY' 3)[0].Id -eq 'y:JPY=X') 'FX alias failed'
Assert (@(Search 'not a real fund あああ' 1).Count -eq 0) 'Unexpected unknown fund mapping'
Assert (@(Search 'BRK-B' 3)[0].Id -eq 'custom:BRK-B') 'Manual ticker candidate failed'
Assert (@(Search '89311199' 1)[0].Provider -eq 'sbi') 'SBI fund source not configured'
$panel = [Activator]::CreateInstance($assembly.GetType('OrbitCalendar.CalendarPanel'), [object[]]@([Action]{},[Action]{}))
$type = $panel.GetType(); $root = $panel.Content.Child
function InvokePanel($name, $arguments = @()) { $type.GetMethod($name,$instance).Invoke($panel,[object[]]$arguments) }
function Click($name) { $root.FindName($name).RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)) }
function Snapshot($name) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(458,542,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($root); $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $folder $name)); try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
function ActionNamed($name) {
    foreach ($row in $root.FindName('MarketResults').Children) {
        if ($row -isnot [Windows.Controls.Border]) { continue }
        foreach ($button in $row.Child.Children[1].Children) {
            if ([Windows.Automation.AutomationProperties]::GetName($button) -eq $name) {
                $button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)); return
            }
        }
    }
    throw ('Action not found: '+$name)
}
try {
    $panel.Open(); $type.GetField('outside',$instance).GetValue($panel).Enabled = $false; Pump 1900
    Click 'MarketTab'
    WaitUntil { $null -eq $type.GetField('loading',$instance).GetValue($panel) }
    Assert ($root.FindName('DataTitle').Text.Contains('日経平均') -and $root.FindName('Plot').Children.Count -gt 10) 'Nikkei chart failed'
    Snapshot 'default-market.png'
    $root.FindName('MarketChoices').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    WaitUntil { $null -eq $type.GetField('loading',$instance).GetValue($panel) }
    Assert ($root.FindName('DataTitle').Text.Contains('円/ドル') -and $root.FindName('Plot').Children.Count -gt 10) 'USDJPY chart failed'
    Snapshot 'usdjpy.png'
    Click 'ManageMarkets'; Pump 300; Snapshot 'registered-markets.png'
    Click 'SearchMarkets'; $root.FindName('MarketQuery').Text = 'オルカン'; Pump 300
    Assert ($root.FindName('MarketResults').Children.Count -gt 0) 'Search UI failed'
    ActionNamed '価格取得を確認 0331418A'
    WaitUntil { $null -eq $type.GetField('checkingMarket',$instance).GetValue($panel) }
    Assert ($root.FindName('MarketMessage').Text.StartsWith('取得成功')) ('Fund CSV failed: '+$root.FindName('MarketMessage').Text)
    Snapshot 'fund-search.png'; ActionNamed '銘柄を追加 0331418A'
    Assert ($items.Count -eq 3) 'Add failed'
    ActionNamed '銘柄を追加 0331418A'
    Assert ($items.Count -eq 3) 'Duplicate added'
    Click 'RegisteredMarkets'; ActionNamed '銘柄を上へ 0331418A'
    Assert ($items[1].Code -eq '0331418A') 'Reorder failed'
    $watch.GetMethod('Reload',$static).Invoke($null,@()) | Out-Null
    Assert ($items.Count -eq 3 -and $items[1].Code -eq '0331418A') 'Persistence/reorder failed'
    Click 'CloseMarketPicker'
    $root.FindName('MarketChoices').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    WaitUntil { $null -eq $type.GetField('loading',$instance).GetValue($panel) }
    Assert ($root.FindName('DataTitle').Text.StartsWith('オルカン')) 'Wrong fund after reorder'
    Snapshot 'all-country.png'
    Click 'ManageMarkets'; Click 'SearchMarkets'; $root.FindName('MarketQuery').Text='03311187'; Pump 300
    ActionNamed '価格取得を確認 03311187'
    WaitUntil { $null -eq $type.GetField('checkingMarket',$instance).GetValue($panel) }
    Assert ($root.FindName('MarketMessage').Text.StartsWith('取得成功')) 'Second fund adapter failed'
    $root.FindName('MarketQuery').Text='89311199'; Pump 300; Snapshot 'sbi-fund-search.png'
    ActionNamed '銘柄を追加 89311199'; Assert ($items.Count -eq 4) 'SBI fund add failed'
    Click 'CloseMarketPicker'
    $root.FindName('MarketChoices').Children[3].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    WaitUntil { $null -eq $type.GetField('loading',$instance).GetValue($panel) }
    Assert ($root.FindName('DataStamp').Text.Contains('基準価額') -and $root.FindName('Plot').Children.Count -gt 10 -and $root.FindName('RefreshData').IsEnabled) 'SBI NAV chart failed'
    Assert ($root.FindName('DataSource').Content.Contains('SBI証券')) 'SBI source attribution missing'
    Snapshot 'sbi-v-sp500.png'
    Click 'ManageMarkets'; Click 'SearchMarkets'; $root.FindName('MarketQuery').Text='存在しない投信あああ'; Pump 300
    Assert ($root.FindName('MarketResults').Children[0] -is [Windows.Controls.TextBlock]) 'Empty search not explained'
    Click 'RegisteredMarkets'
    foreach ($code in @('^N225','0331418A','JPY=X','89311199')) { ActionNamed ('銘柄を削除 '+$code) }
    Assert ($items.Count -eq 0) 'Remove failed'
    Click 'CloseMarketPicker'; Pump 1800
    Assert ($root.FindName('DataTitle').Text -eq '銘柄を追加してください') 'Empty list view failed'
    $watch.GetMethod('Reload',$static).Invoke($null,@()) | Out-Null
    Assert ($items.Count -eq 0) 'Empty watchlist not persisted'
    Click 'ManageMarkets'; Click 'SearchMarkets'; $root.FindName('MarketQuery').Text='7203'; Pump 300
    ActionNamed '銘柄を追加 7203'; Click 'CloseMarketPicker'
    WaitUntil { $null -eq $type.GetField('loading',$instance).GetValue($panel) }
    Assert ($root.FindName('DataTitle').Text.StartsWith('トヨタ') -and $root.FindName('Plot').Children.Count -gt 10) 'Domestic stock chart failed'
    Click 'CalendarTab'; Click 'SettingsButton'; Pump 1900; Snapshot 'settings.png'
    Click 'SettingsMarkets'; Pump 300
    Assert ($root.FindName('MarketPicker').Visibility -eq 'Visible') 'Settings entry failed'
    Click 'BackParticles'; Pump 1900
    Assert ($root.FindName('SettingsPicker').Visibility -eq 'Visible') 'Return to particle settings failed'
    Write-Output ('PASS: catalog aliases/codes, filters, defaults, live FX/stocks/MUFG and SBI funds, add/dedup/order/remove, persistence, empty views, settings navigation')
    Write-Output $folder
}
finally { $panel.Hide(); $panel.Close() }
