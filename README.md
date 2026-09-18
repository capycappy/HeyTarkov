# Hey Tarkov v1.6.1

マイクボタンを押している間に**タスク名・マップ名・脱出地点名・KAPPA品名・鍵の名前**を言うと、
Escape from Tarkov の Wiki の該当ページをブラウザで開くデスクトップアプリ。
**日本語（カタカナ読み）と英語の両対応**、**日本語 Wiki と英語 Wiki の両対応**。

## アプリは Wiki に一切アクセスしません

一覧は**アプリに埋め込まれた固定データ**です。
アプリが実行中に Wiki へリクエストを送ることはありません。やっているのは
**ブックマークと同じく、ブラウザで該当ページを開くこと**だけです。

シーズンやイベントで追加されたタスクは、アプリを更新するまで出てきません。

アプリが通信するのは1点のみ、**GitHub Releases に新しいバージョンがあるかの確認**です。

## ダウンロード

[Releases](https://github.com/capycappy/HeyTarkov/releases) から ZIP を取得して展開するだけ。
インストーラはない。

| | サイズ | 必要なもの |
|---|---|---|
| `HeyTarkov-v1.6.1-win-x64.zip` | 42MB | **なし**（これを選べばよい） |
| `HeyTarkov-v1.6.1-win-x64-framework-dependent.zip` | 0.6MB | .NET 10 Desktop Runtime |

## つかいかた

1. `HeyTarkov.exe` を起動（Alt+Tab で切り替えて使う想定）
2. 上部のカードで 言語 / Wiki / ブラウザ / 入力デバイス を選ぶ（一度きり）
3. **丸いマイクボタンを押しっぱなし**にして、タスク名を言う
4. 離すと認識 → 候補が出る。確信度が高ければそのままブラウザで開く

聞き取り中は認識途中の文字列がリアルタイムで表示される。認識しきれなかった場合も、
何と聞こえたかを表示したうえで近い候補を並べる。

ボタンにフォーカスがある状態なら **Space キー長押し** でも同じ。
音声を使わずキーボードで探すこともできる（下の検索ボックス）。英語で打てば英語の名前から、
日本語（ひらがな・カタカナ・漢字）で打てば日本語の名前から探す（「こんこるでぃあ」「廃工場」など）。

言語や Wiki を切り替えた直後は準備に約1秒かかる。その間に押しても、
押したままにしておけば準備ができ次第そのまま聞き取りが始まる。

### 途中まで言えば候補が出る

**フルネームを言う必要はありません。**「ブロードキャスト」だけで `Broadcast - Part 1〜5`
が候補に並びます。途中までの場合は候補を出すだけで、自動では開きません
（フルネームを言ったときだけ自動で開きます）。

候補は名前順に並びます（`Part 10` は `Part 2` の後）。

### 言えるもの

| 種別 | 件数 | 例 |
|---|---|---|
| タスク | 902 | 「ウェット ジョブ パート フォー」 |
| マップ | 15 | 「グラウンドゼロ」 |
| 脱出地点 | 143 | 「グラウンドゼロ エマーコムチェックポイント」 |
| KAPPA品 | 44 | 「ゴールデンエッグ」 |
| 鍵 | 217 | 「マークの刻まれた廃工場の鍵」（鍵ボタン ON のとき） |

脱出地点はマップ名を省略できます。`Emercom Checkpoint` のように複数マップに
同名がある場合は、マップ名を付けると1つに決まります。
候補リストには種別と陣営が出ます（`[出口] Emercom Checkpoint (PMC)`）。

| モード | 「Wet Job - Part 4」の言い方 |
|---|---|
| 日本語 | ウェット ジョブ パート フォー / … パート ヨン |
| 日本語（スペル読み） | ダブリュー イー ティー　ジェー オー ビー　ピー エー アール ティー　フォー |
| 英語 | wet job part four / wet job part 4 |

**発音がわからないタスクは、アルファベットを読み上げれば通る。**
`Fall Ailment` → `エフ エー エル エル　エー アイ エル エム イー エヌ ティー`
候補リストには各タスクのカタカナ読みも表示されるので、言い方はそこで確認できる。

### イベントタスク

シーズンイベントのタスクには **`EVENT` のバッジ**が付き、候補リストで色が変わる。
同じように、ストーリーのタスク（トレーダー欄が `Story` のもの）には **`STORY` のバッジ**が付く。

**イベント名を言うとそのイベントのタスクだけが並ぶ。**

```
「コード ブリーチ」 → KORD BREACH の 19 件
```

日本語 Wiki にまだ記事が無いタスクは、**英語 Wiki に切り替えれば同じ言葉のまま探し直される。**

### KAPPA品のチェックリスト

Fence の `Collector` タスクで納品する **44品目**の所持チェックリスト。
下部の **KAPPA品** ボタンにいまの進捗が出て、押すとチェックリストが開く。

```
アイコンの文字 ▲    アイテム名                  日本語名
☑ BeardOil          Deadlyslob's beard oil      DeadlySlob's ビアードオイル
☐ Plague mask       Pestily plague mask         Pestily ペストマスク
```

左の列は**ゲームがアイコンの上に出している文字**なので、スタッシュの画面と見比べやすい。
どの列でも並べ替えでき、絞り込みは3列すべてを対象にする。

- チェックは押した瞬間に保存され、`%LOCALAPPDATA%\HeyTarkov\collector.json` に残る
- アプリを更新しても記録は消えない。直前の版は `collector.json.bak` に残る
- 記録ファイルを読めなかったときはその旨を表示し、上書きしない
- シーズンが変わったときのために **すべて解除** がある（確認あり）
- 行をダブルクリックでその品目の Wiki ページが開く

品目名は読み上げでも引ける（「ゴールデンエッグ」でページが開く）。

### 鍵

部屋や金庫の鍵は数が多く、名前も似ています（`room key` で終わるものだけで30種類）。
そのまま混ぜると普通の検索がすべて鍵で埋まるので、**鍵は専用のボタンの中にあります。**

マイクボタンと検索ボックスの間にある**鍵の絵のボタン**を押すと、鍵だけが出ます。
もう一度押すと元に戻り、鍵は出てこなくなります。

```
鍵ボタン ON   「マークの刻まれた廃工場の鍵」 → Abandoned factory marked key
              「ドーム ルーム サンイチヨン」 → Dorm room 314 marked key
鍵ボタン OFF  ふだんどおり（タスク・マップ・脱出地点・KAPPA品）
```

- **ゲームを日本語でプレイしているときの鍵の名前でも言えます**（ゲーム画面と同じ名前。英語の名前でも言えます）
- 英字の入った名前は読み方どおりに言えます（`RB-AM の鍵` →「アールビー エーエム の鍵」、`OLI` は「オリ」でも「オーエルアイ」でも）
- 部屋番号は「サンイチヨン」でも「サンビャクジュウヨン」でも通ります
- 行にはその鍵が**どのマップのものか**と、ゲーム画面での日本語名が出ます
- 鍵ボタンが OFF のときに鍵の名前を打ち込むと、「鍵が◯件あります」と知らせます。ボタンを押せば出ます

### Wiki の選択

| | |
|---|---|
| 日本語 Wiki | [wikiwiki.jp/eft](https://wikiwiki.jp/eft/) |
| 英語 Wiki | [escapefromtarkov.fandom.com](https://escapefromtarkov.fandom.com/wiki/Quests) |

**選択中の Wiki にページがある項目だけが認識・検索の対象**になる。
Wiki を切り替えると、**直前に言った言葉でそのまま探し直す**（言い直さなくてよい）。
選択中の Wiki に無くて**他方にあるものは、赤字で「英語Wikiにあり」と候補に出る**。
その行を開くと、ページがある側の Wiki が開く。

**脱出地点は、日本語 Wiki では出口そのものへ、英語 Wiki では出口の一覧表へ飛ぶ。**

### 表示言語

**画面は日本語と英語の両方に対応している。** 初回起動時は Windows の表示言語に合わせる
（日本語なら日本語、それ以外は英語）。上部の「表示言語」で切り替えられる。

表示言語を変えると、話す言語と Wiki も一緒に切り替わる。そのあとでそれぞれ個別に変えることもできる
（英語表示のまま日本語 Wiki を開く、といった組み合わせも可能）。

表示言語を変えるとアプリが再起動する。ウィンドウの位置と大きさはそのまま引き継がれる。

### 画面

**マイクボタンが入力レベル計を兼ねる**。押している間だけ下から満ちる。

候補は1行に**種別・名前・トレーダー・一致度・カタカナ読み**の列を持ち、
タスク / マップ / 出口が色で分かれる。一覧の上の見出し（名前・読み・トレーダー／マップ・一致度）を
押すと、その列で並べ替わる。もう一度押すと逆順になる。

100% から 200% までの拡大率に対応し、別解像度のモニターへ移してもレイアウトは崩れない。

**ウィンドウの位置と大きさは前回終了時のものを覚える。** どのモニターのどこに置いたかも含む。
そのモニターが無くなっていたら中央に戻す。
最大化したまま閉じれば最大化で開く。

### 入力デバイスとブラウザ

入力デバイスはドロップダウンで選べる（オーディオインターフェースで入力が複数ある場合など）。
`再検出` は起動後に機器を繋ぎ替えたときに使う。

ブラウザは Windows に登録されているものから選択できる（既定のブラウザ / Chrome / Edge /
Firefox / Brave など）。選択はすべて記憶される。

**「レベル確認」ボタン**は、マイクの音が届いているかだけを確認する。
丸が下から満ちるのが今の音量、残る線がこれまでの最大値、その線の色が判定。
レベルの数値は最下段に **dBFS** で出る。

| ピーク | 判定 |
|---|---|
| -50 dBFS 未満 | 無音（何も入っていない） |
| -50 〜 -35 | 小さすぎ（ゲインを上げる） |
| -35 〜 -3 | 十分 |
| -3 以上 | 大きすぎ（歪む恐れ） |

### 表示テーマ

下部の「表示」で **システムに従う / ライト / ダーク** を選べる（初期値はシステム追従）。
切り替えるとアプリが自動で再起動する。

### バージョン確認

起動時に GitHub Releases を確認し、新しいバージョンがあれば画面上部に知らせる（クリックで開く）。
オフラインのときなどは何も出さない。

タスク一覧はアプリ本体と一緒に更新されるので、**この通知が「新しいタスクが入った版が出た」の合図**になる。

## 必要なもの

- Windows 11（自己完結版ならランタイムの導入は不要）
- 日本語モード: 日本語の音声認識（Windows 標準で入っていることが多い）
- 英語モード: 英語(米国)の音声認識
  - 設定 → 時刻と言語 → 言語と地域 → English (United States) → 言語のオプション → 音声認識
  - 管理者 PowerShell なら `tools\install-en-speech.ps1`
  - ダウンロードに非常に時間がかかることがある（進捗が動かなくても待つ）

インストール済みの認識エンジンはアプリ下部のステータス行に表示される。

## アンチチートについて

このアプリはゲームに一切触れない。以下を**行わない**:

- ゲームプロセスのメモリ読み書き / DLL インジェクション
- DirectX フック、ゲーム内オーバーレイ
- ゲームウィンドウへの合成入力送信（SendInput 等）
- カーネルドライバ
- グローバルホットキー（自ウィンドウのボタンで完結するため不要）
- EXE のパッキング・難読化

やっているのは「マイクから録音」「文字列一致」「ブラウザを起動」の3つだけで、
ブラウザのショートカットをダブルクリックするのと同じ挙動しかしない。

音声取り込みには NAudio（WinMM のみ）を使っている。標準の録音APIを使うだけで、
フックもドライバも含まない。

## ビルド

コマンドは PowerShell のもの。ビルドの生成物はリポジトリの外に出る。

| | 場所 |
|---|---|
| ビルド中間物 | `%LOCALAPPDATA%\HeyTarkov\build\` |
| 完成品（exe・配布 ZIP） | `%USERPROFILE%\app\HeyTarkov\` |

```
dotnet publish src\HeyTarkov -c Release -o "$env:USERPROFILE\app\HeyTarkov"
```

`%USERPROFILE%\app\HeyTarkov\HeyTarkov.exe` が単一ファイルの成果物。付随ファイルは不要。

配布前に次を実行し、`PASS` 以外なら配布しない。

```
dotnet run --project tools\PathCheck -- "$env:USERPROFILE\app\HeyTarkov\HeyTarkov.exe"
```

### 配布用 ZIP

ZIP は `%USERPROFILE%\app\HeyTarkov\` に作る。中身は `HeyTarkov.exe` `LICENSE` `README.md` の3つ。

```
$v = "1.6.1"
$stage = "$env:LOCALAPPDATA\HeyTarkov\build\publish"
$out = "$env:USERPROFILE\app\HeyTarkov"

dotnet publish src\HeyTarkov -c Release -o "$stage\HeyTarkov-v$v-win-x64" -p:SelfContained=true -p:PublishSingleFile=true
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\HeyTarkov\build\bin\HeyTarkov", "$env:LOCALAPPDATA\HeyTarkov\build\obj\HeyTarkov"
dotnet publish src\HeyTarkov -c Release -o "$stage\HeyTarkov-v$v-win-x64-framework-dependent"

foreach ($name in "HeyTarkov-v$v-win-x64", "HeyTarkov-v$v-win-x64-framework-dependent") {
  dotnet run --project tools\PathCheck -- "$stage\$name\HeyTarkov.exe"
  Remove-Item "$stage\$name\*.pdb" -ErrorAction SilentlyContinue
  Copy-Item LICENSE, README.md "$stage\$name"
  Compress-Archive -Path "$stage\$name\*" -DestinationPath "$out\$name.zip" -Force
}
```

自己完結版を publish した後は、上の2行目のとおり `build\bin\HeyTarkov\` と `build\obj\HeyTarkov\` を消してから次をビルドする。

### タスク一覧の更新

```
dotnet run --project tools\CatalogBuilder
```

両 Wiki から一覧を取り直して `src\HeyTarkov\tasks.json` を書き換える。
実行後にアプリをビルドし直すと新しい一覧が埋め込まれる。
その後 `--selftest` を実行し、読みが足りない単語があれば `japanese-lexicon.json` に追記する。

### アイコン

```
dotnet run --project tools\IconMaker
```

`src\HeyTarkov\app.ico` と `docs\icon\icon-*.png` を生成する。

### 診断コマンド

```
HeyTarkov.exe --selftest     # 合成音声で英語・日本語の認識を通しで検証
HeyTarkov.exe --mictest 6    # 実マイクで6秒録音し、全段階を報告
HeyTarkov.exe --devices      # 入力デバイスを列挙し、実際に開いて録れるか確認
HeyTarkov.exe --streamdiag   # 音声入力経路の診断
HeyTarkov.exe --checkupdate  # 起動時のバージョン確認を実行して報告
HeyTarkov.exe --collector    # KAPPA品の記録の場所と読み込み結果を報告
```

結果は `%LOCALAPPDATA%\HeyTarkov\` に `*-report.txt` として出力される。

## ライセンス

MIT License — Copyright (c) 2026 capycappy（[LICENSE](LICENSE)）

## 出典

タスク名は Escape from Tarkov（Battlestate Games）のゲーム内名称。
リンク先は [wikiwiki.jp/eft](https://wikiwiki.jp/eft/) と
[Escape from Tarkov Wiki (Fandom)](https://escapefromtarkov.fandom.com/) の各ページで、
本アプリは両 Wiki の本文を一切複製・再配布しない。

このアプリは Battlestate Games および各 Wiki とは無関係の非公式ツール。
