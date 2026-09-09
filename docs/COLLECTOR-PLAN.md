# Collector チェックリスト — 構成案

Fence の [Collector](https://wikiwiki.jp/eft/Fence/Collector) は、見つけたアイテムを
全部集めて納品するタスク。何を持っていて何が足りないかを覚えていられないので、
アプリ側にチェックリストを持たせる。

**2026-09-10 に実装し、v1.5.0 として `main` に入れた。** 以下は作る前に立てた計画と、
作りながら決まったこと（§6）の記録。実装が計画から離れた点は次のとおり:

- 画面は案A（別ウィンドウ）を採用。`MainForm` への変更はボタン1つに収まった
- カテゴリ色分けは取り下げ、ゲームがアイコンに出す短縮名を出す形にした（§6.5）
- 短縮名の取得元は tarkov.dev 1本の想定だったが、丸一日落ちていたため
  ゲームロケールのミラーと Wiki の冒頭文を加えた3系統になった
- 行のどこでもクリックでチェック → **チェックボックスのみに戻した**。
  Wiki を開くダブルクリックが2回のチェックになり、「未所持だけ」表示だと
  意図しない品目が次々消えるループになったため
- 読み上げ対象に入れた影響は実測ゼロ（文法 4,494 語、自己テスト 9/9・17/17 のまま）

---

## 0. 調べてわかったこと

| | |
|---|---|
| 品目数 | **44**（英語Wiki `Collector` の `==Objectives==` を実際に取得して数えた） |
| 英語Wikiの形 | `* Hand over the found in raid item: [[Antique axe]]` の箇条書き。**機械的に取れる** |
| 日本語Wikiの形 | 同じ44品目。Collectorページの表は**英語名のみ**。日本語名は各アイテムの個別ページにある（§6.2） |
| 日本語のアイテムページ | `https://wikiwiki.jp/eft/<英語名>` で確定。44件すべて実在を確認済み。見出しが `Golden egg / ゴールデンエッグ` の形で**日本語名を持っている** |

44品目の一覧は英語Wikiから取得済み（`42 Signature Blend English Tea` 〜 `WZ Wallet`）。

---

## 1. アイテム一覧をどこから持ってくるか

**ビルド時に取り込む。実行時にWikiを見にいかない**（これは動かせない制約）。

`tools/CatalogBuilder/` に `Collector.cs` を足す。やることは `Seasons.cs` とほぼ同じ:

1. 英語Wiki APIで `Collector` の本文を取る
2. `==Objectives==` から `Hand over the found in raid item: [[名前]]` を抜く
3. 各アイテムの日本語ページの有無を1件ずつ確認する（`Tasks.cs` の
   `FindJapaneseEventPagesAsync` と同じやり方）

出力先は **`tasks.json` に相乗り**。新しいデータファイルは作らない。

理由: 埋め込み・バージョン管理・件数表示・自己テストの仕組みが全部そのまま使えるため。
`WikiEntry` に `EntryKind.Item` を1つ足すだけで、**読み上げてWikiを開く動線がタダで手に入る**
（「ゴールデンエッグ」と言えばそのアイテムのページが開く）。

```
EntryKind { Task, Map, Extract, Item }   // Item を追加
WikiEntry { Kind = Item, Group = "Collector", Name = "Golden egg", ... }
```

---

## 2. チェック状態をどこに保存するか

`%LOCALAPPDATA%\HeyTarkov\collector.json` — **`settings.json` とは別ファイル**。

```json
{
  "Version": 1,
  "Checked": ["Antique axe", "Golden egg"],
  "UpdatedAt": "2026-09-08T00:00:00+09:00"
}
```

**別ファイルにする理由。** `settings.json` は消えても困らない設定だが、これは
ユーザー自身の記録で、失うと集め直しになる。分けておけば設定の書き込みが失敗しても
巻き添えにならないし、これ1つコピーすれば別PCに持っていける。

**番号ではなく名前で持つ理由。** Wiki更新で品目が増減しても記録がズレない。
逆に、チェック済みだったアイテムがタスクから外れた場合も、**ファイルからは消さない**
（ユーザーのデータを勝手に捨てない）。表示から外すだけにする。

`dist` を差し替えても `%LOCALAPPDATA%` は消えないので、更新しても記録は残る。

---

## 3. 画面をどうするか — ここが一番の論点

いまのメイン画面は 680×740 の縦一列で、**声で押すリモコン**として作ってある。
44行のチェックリストは性質が違う。3案:

| | 案 | 良い点 | 悪い点 |
|---|---|---|---|
| **A** | **別ウィンドウ**（`CollectorForm`）。設定カードかフッターにボタンを1つ足して開く | メイン画面を一切壊さない。44行に十分な広さが取れる。**気に入らなければボタン1つ消すだけで撤退できる** | ウィンドウが2枚になる |
| B | メイン画面にタブ／モード切替。結果カードと差し替える | 1枚で完結 | せっかく整えたレイアウトを作り直す。撤退が面倒 |
| C | 「コレクター」と言うと候補リストに44件出る。そこにチェックボックス | 既存の動線に乗る | 候補リストは**一時的な検索結果**の場所。状態を持たせる場所ではない |

**A を推す。** 理由は主に撤退のしやすさ。今回は「良かったらマージ」なので、
メイン画面に手を入れない案が一番安全。

### 画面の中身（案A）

```
┌─────────────────────────────────────┐
│  Collector            12 / 44        │   ← 進捗
│  ┌───────────────────────────────┐   │
│  │ 🔍 絞り込み          [ ] 残りだけ │   │   ← 打って絞る／未所持だけ表示
│  └───────────────────────────────┘   │
│  ☑ Antique axe                       │   ← 名前をクリックでWikiを開く
│  ☐ Axel parrot figurine              │
│  ☑ BEAR Buddy plush toy              │
│  ...                                 │
└─────────────────────────────────────┘
```

- 並びはWikiと同じ順（アルファベット順）。並べ替えではなく**「残りだけ」フィルタ**で対処する
- 名前クリック → 選択中のブラウザでそのアイテムのWikiページ（既存の `BrowserLauncher` を使う）
- チェックした瞬間に保存（「保存」ボタンは作らない。忘れて閉じたら意味がない）
- 見た目は既存の `Card` / `Painting.cs` / `Theme.cs` に乗せる。ダーク／ライト両対応

---

## 4. 声との関係 — 段階を分ける

このアプリでやる意味があるのはここだが、**一度に全部やらない**。

**第1段階（まずここまで）**
- アイテム名を `EntryKind.Item` として文法に入れる → 言えばWikiページが開く
- チェックは画面のクリックだけ
- **文法が44語分増える影響を必ず測る**。今日 `ショーテージ` が語彙密度で棄却されたのを見たばかりなので、
  自己テストの英語9件・日本語17件が落ちないか確認してから進める。落ちるなら
  「アイテムは打ち込み検索だけ、読み上げ対象にしない」に切り替える

**第2段階（使ってみてから決める）**
- Collector画面を開いている間だけ、アイテム名を言うとチェックが付く

「言う＝ブラウザが開く」という今の約束事と衝突するので、**使ってみてから**判断したい。

---

## 5. 触るファイル

| ファイル | 変更 |
|---|---|
| `tools/CatalogBuilder/Collector.cs` | 新規。44品目の取得 |
| `tools/CatalogBuilder/Program.cs` | 上を呼ぶ |
| `src/HeyTarkov/WikiEntry.cs` | `EntryKind.Item` を追加 |
| `src/HeyTarkov/CollectorRecord.cs` | 新規。`collector.json` の読み書き |
| `src/HeyTarkov/CollectorForm.cs` | 新規。画面 |
| `src/HeyTarkov/MainForm.cs` | 開くボタン **1つだけ** |
| `src/HeyTarkov/Strings.cs` | 文言（日英） |
| `src/HeyTarkov/SelfTest.cs` | 44品目が引けるか／文法が壊れていないか |

`MainForm.cs` への変更をボタン1個に抑えるのが、撤退可能性の肝。

---

## 6. 決まったこと（2026-09-08）

### 6.1 品目差分 — **無し**

両Wikiとも44品目、中身は同一。違うのは同じアイテムの綴り2件だけで、
**それぞれのWikiは自分の綴りのページしか持たない**（相互リダイレクトも無い）。

| | 英語Wiki | 日本語Wiki | 正 |
|---|---|---|---|
| CD | `DesmondPilak CD` | `Desmond Pilak CD` | — |
| 水 | `Bottle of YMXC water` | `Bottle of YXMC water` | **YMXC**（日本語Wikiが誤記） |

表示・読み上げは**英語Wikiの綴り**を使う。日本語Wikiのリンク先だけ向こうの綴りにする。
`WikiEntry` は名前とURLを別々に持つので構造の変更は不要。

**ペアリングは正規化では足りない。** `DesmondPilak` ↔ `Desmond Pilak` は空白を潰せば一致するが、
`YMXC` ↔ `YXMC` は文字が入れ替わっているので一致しない。完全一致 → 既存の Levenshtein の順に試し、
**あいまい一致した組はビルドログに出して人間が確認する**。決め打ちにすると、
どちらかのWikiが誤記を直した瞬間に壊れる。

### 6.2 表示 — 日本語Wiki選択時は英語と日本語を併記

```
Golden egg / ゴールデンエッグ
Baddie's red beard              ← 日本語名が無いものは英語だけ
```

44件中 **30件に日本語名があり、14件は無い**。無いものはWikiに
「日本語名称無し（英名称と同じ）」と**文字列で書かれている**ので、
これを日本語名として取り込まないよう弾くこと。`FireKlean` は先頭に `#` が付くなど、
Wiki記法の残りかすも落とす。

`WikiEntry` に `JapaneseName`（null許容）を足す。読み上げ対象は英語名のまま。

### 6.3 画像 — **使わない**

両Wikiとも `CC BY-NC-SA`（表示 - 非営利 - 継承）。**NC は MIT と両立しない**ので、
Wikiの素材をこの配布物に混ぜることはできない。

それ以前に、両Wikiのフッターが
`Game content and materials are trademarks and copyrights of Battlestate Games and its licensors.`
と明記しているとおり、**アイテムアイコンはWikiのものではない**。Wikiは持っていない権利を
CC で配れないので、CC BY-NC-SA が掛かっているのは編集者の書いた文章のほうだけ。

実行時のホットリンクも駄目（自分ルールにもFandomの規約にも反する）。

見分けが付かない問題は、**自分で描いた記号か色分け**で対処する。画像が見たければ行をクリックしてWikiを開く。

### 6.4 `Wikis.cs` に穴がある — 先に直す

wikiwiki.jp は **1.2秒間隔でも 429 Too Many Requests を返す**（実測: 5件目で発生）。
いまの `GetAsync` は「アクセス確認中」のBOT判定しか見ておらず、
429 は `catch (Exception)` に落ちて **即 `null` を返す**。
このままアイテムページを44件叩くと、ほとんどが「日本語ページ無し」と誤判定される。

429 を見て待ち直す処理を入れる（20秒から倍々、上限120秒で実測は通った）。
**これは既存のタスク取得にも効く修正**なので、Collector とは別に先に入れてよい。

### 6.5 表示するのは短縮名 — ゲーム画面と同じ文字

スタッシュのアイテムは、アイコンの上に**短い名前**が出ている。

```
Glorious   Plague mask   Viibiin   Axel   BeardOil   Badge   Mazoni   DRD   WZ   Tigzresq
```

照合するとき人が実際に読んでいるのはこれなので、一覧にはこれを出す。
`Glorious E lightweight armored mask` というフルネームは、目で突き合わせる役には立たない。

これはゲーム内の `shortName`。**両Wikiとも載せていない**（Fandomの infobox にも無い。
あるのはBSGのアイテムID `node` のほう）。取得元は `tarkov.dev` の公開GraphQL API。

- 取ってくるのは**短い機能的な文字列**であって、絵ではない。すでに載せているタスク名1,057件と同じ立場
- `tarkov-api` は GPL-3.0 だが、それは**サーバーsoftware のライセンス**であって、
  APIが返すデータに伝播するものではない。こちらのMITに影響しない
- これもビルド時に1回引くだけ。実行時には触らない

当初案の「カテゴリ色分け」は**取り下げ**。あれは私が勝手に決めた分類で、
ゲーム内に存在しない情報を足すだけだった。短縮名のほうが本物。

### 6.6 一覧の行はこうなる

```
☑ BeardOil     Deadlyslob's beard oil / DeadlySlob's ビアードオイル
☐ Plague mask  Pestily plague mask / Pestily ペストマスク
☐ DRD          DRD body armor
```

左に短縮名（目で探す用）、右にフルネーム（読み上げ・リンク用）。
日本語Wiki選択時のみ日本語名を併記（§6.2）。

---

## 7. 44品目の対応表

| # | 英語Wiki（＝表示名） | 日本語Wikiのページ名 | 日本語名 |
|---|---|---|---|
| 1 | 42 Signature Blend English Tea | 同じ | 42 シグニチャーブレンド 英国紅茶 |
| 2 | Antique axe | 同じ | アンティークの斧 |
| 3 | Axel parrot figurine | 同じ | オウムの Axel の置物 |
| 4 | Baddie's red beard | 同じ | — |
| 5 | BakeEzy cook book | 同じ | BakeEzy レシピ本 |
| 6 | Battered antique book | 同じ | アンティークの本 |
| 7 | BEAR Buddy plush toy | 同じ | BEAR バディのぬいぐるみ |
| 8 | Bottle of YMXC water | **Bottle of YXMC water** | — |
| 9 | Can of Dr. Lupo's coffee beans | 同じ | Dr. Lupo's コーヒー豆 |
| 10 | Can of GigaBeef meat | 同じ | GigaBeefの肉の缶詰 |
| 11 | Can of RatCola soda | 同じ | ラットコーラ |
| 12 | Deadlyslob's beard oil | 同じ | DeadlySlob's ビアードオイル |
| 13 | DesmondPilak CD | **Desmond Pilak CD** | — |
| 14 | Domontovich ushanka hat | 同じ | — |
| 15 | DRD body armor | 同じ | — |
| 16 | Dunduk floppy disk | 同じ | — |
| 17 | Fake mustache | 同じ | 付け髭 |
| 18 | FireKlean gun lube | 同じ | FireKlean ガンオイル |
| 19 | French bakery baguette | 同じ | フランスパンのバゲット |
| 20 | Gingy keychain | 同じ | Gingy のキーホルダー |
| 21 | Glorious E lightweight armored mask | 同じ | — |
| 22 | Golden 1GPhone smartphone | 同じ | 1GPhone オレンジゴールド |
| 23 | Golden egg | 同じ | ゴールデンエッグ |
| 24 | Inseq gas pipe wrench | 同じ | Inseq ガスパイプレンチ |
| 25 | JohnB Liquid DNB glasses | 同じ | — |
| 26 | LM KC-130 model aircraft | 同じ | — |
| 27 | Loot Lord plushie | 同じ | ルートロードのぬいぐるみ |
| 28 | LVNDMARK's rat poison | 同じ | LVNDMARK's 殺鼠剤 |
| 29 | Mazoni golden dumbbell | 同じ | — |
| 30 | Missam forklift key | 同じ | Missamのフォークリフトの鍵 |
| 31 | Nut Sack balaclava | 同じ | — |
| 32 | Pestily plague mask | 同じ | Pestily ペストマスク |
| 33 | Press pass (issued for NoiceGuy) | 同じ | NoiceGuy のプレスカード |
| 34 | Raven figurine | 同じ | カラスの置物 |
| 35 | SheefGG piggy bank | 同じ | — |
| 36 | Shroud half-mask | 同じ | Shroud ハーフマスク |
| 37 | Silver Badge | 同じ | シルバーバッジ |
| 38 | Smoke balaclava | 同じ | Smoke バラクラバ |
| 39 | Tamatthi kunai knife replica | 同じ | Tamatthi のレプリカのクナイ |
| 40 | Tigzresq splint | 同じ | — |
| 41 | Veritas guitar pick | 同じ | Veritas ギターピック |
| 42 | Video cassette with the Cyborg Killer movie | 同じ | 映画 "サイボーグ・キラー" のビデオ |
| 43 | Viibiin sneaker | 同じ | — |
| 44 | WZ Wallet | 同じ | WZ ウォレット |

---

## 8. やらないこと

- ゲームプロセスには一切触らない。所持品の自動読み取りはしない（アンチチート）
- 実行時のWikiアクセスはしない
- クラウド同期はしない。記録はこのPCのローカルファイルのみ
- Wikiの画像は同梱もホットリンクもしない（§6.3）
