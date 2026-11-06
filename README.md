# Orbit Calendar

Windowsのタスクバーの日時をクリックすると開く、粒子でできたカレンダーです。
カレンダーのほかに、株価・天気・ニュース・運行情報・タイマーをまとめて見られます。

[Windows版をダウンロード](https://github.com/NaitoMitsuharu/OrbitCalendar/releases/latest/download/OrbitCalendar.exe) · [リリース一覧](https://github.com/NaitoMitsuharu/OrbitCalendar/releases)

![粒子が動き、カレンダーを組み上げる様子](docs/assembly.gif)

## カレンダー

週番号・曜日番号・祝日と、今日の天気・気温をひと目で確認できます。スクロールで月を切り替えられます。

![週番号・曜日番号と今日の天気を表示するカレンダー](docs/calendar.png)

![日付が組み替わる月送り](docs/month-change.gif)

## 株価

日経平均やドル円、個別株、投資信託（オルカンなど）のチャートを表示します。名前や愛称で検索して銘柄を追加できます。

| チャート | 銘柄の検索 |
| --- | --- |
| ![日経平均のローソク足](docs/market.png) | ![オルカンの検索結果](docs/market-search.png) |

## 天気

1時間先までの雨雲の動きと、3日間の天気予報を表示します。自宅や職場など、よく見る地点を登録して切り替えられます。

| 雨雲 | 3日間の天気 |
| --- | --- |
| ![雨雲と気温の推移](docs/weather.png) | ![今日・明日・明後日の天気](docs/weather-daily.png) |

![複数の地点を切り替える様子](docs/multiple-weather-places.png)

## ニュースと運行情報

ニュースは「総合」「経済」「IT」から1記事ずつ表示します。運行情報は横浜周辺の路線の遅れや運転見合わせを表示します。

| ニュース | 運行情報 |
| --- | --- |
| ![ニュースの見出し](docs/news.png) | ![鉄道の運行情報](docs/transit.png) |

## タイマー

時間を選んでスタートできます。ウィンドウを閉じても小さな窓に残り時間が表示され、終わると音と通知で知らせます。

![時間を選ぶ様子](docs/time-selection.gif)

![閉じても残るタイマー](docs/timer.png)

## 見た目のカスタマイズ

歯車ボタンから、粒子の見た目・組み上がり方・読み込み中のアニメーション・速さを選べます。

| 流れ込み | 奥行き |
| --- | --- |
| ![粒子が流れ込む様子](docs/assembly-flow.gif) | ![粒子が奥から集まる様子](docs/assembly-depth.gif) |

![読み込みアニメーションの種類](docs/loading-designs.gif)

## 使い方

1. `OrbitCalendar.exe` をダウンロードしてダブルクリックします（インストール不要・Windows 11）。
2. タスクバーの日時をクリックすると開きます。Escや画面の外をクリックすると閉じます。
3. 終了するときは、日時を右クリックして「Orbit Calendar を終了」を選びます。

歯車の設定から、Windowsのサインイン時に自動で起動するようにできます。新しいバージョンが出たときはアプリ内から更新できます。
