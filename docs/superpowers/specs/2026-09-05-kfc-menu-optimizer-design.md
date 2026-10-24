# KFCメニュー最安値セット計算アプリ 設計書

- 作成: 2026-09-05
- 改訂: 2026-10-15（コンソールアプリ → Blazor WebAssembly + 静的ホスティングに変更。menu.jsonを products / options / choices 形式に変更）
- 改訂: 2026-10-17（目的を「AI駆動開発を覚える」に変更し、コードはAIが書く形に変更。自動テストを追加。探索方法をメモ化付きの再帰探索に変更）
- ステータス: 承認済み

## 背景・目的

KFCのメニュー（単品・セット）には、同じ商品（チキン、ポテト、ドリンクなど）を異なる価格・組み合わせで提供するものが複数ある。欲しい商品ごとの必要数を入力すると、それを満たす購入方法のうち最も安いものを計算して表示するアプリを作る。

スマホのブラウザで使えるようにする。計算はBlazor WebAssemblyでブラウザ内で完結させ、サーバーは持たない。サイトはGitHub Pagesなどの無料の静的ホスティングに置く。

**目的と役割分担:** AI駆動開発（設計 → 実装計画 → AIによる実装 → レビュー）の進め方を覚えることが目的。ユーザーは要件の判断・レビュー・確認を行い、コードはAI（Claude）が実装計画に沿って書く。

## スコープ

### 今回実装する範囲
- 画面に商品ごとの「必要数」の入力欄を表示する（初期値はmenu.jsonの値）。「余剰の上限」の入力欄は2026-10-24に削除し、画面では余剰を常に0にして最安を求める（下の条件は計算ロジックの仕様として残る）
- 「計算」ボタンで、条件を満たす購入方法のうち合計金額が最小のものを探索する
  - 各商品の合計個数が「必要数 以上、必要数 + 余剰の上限個数 以下」であること
  - 余剰の上限個数をすべて0にすれば「過不足なく一致」になる（旧設計のv1）。0より大きくすれば、余分に付いてきても安い方を選べる（旧設計のv2）
- 結果として「購入選択肢名 × 個数」（選べる枠で何を選んだかも含む）と合計金額を表示する
- 条件を満たす購入方法が存在しない場合はその旨を表示する
- 最安の下に、セットを使った「おすすめ」を最大3つ表示する（詳細は docs/superpowers/specs/2026-10-24-set-recommendation-design.md）
- 計算ロジック（KfcMenu.Core）の自動テスト（xUnit）
- GitHub Pagesへのデプロイ

### 今回の範囲外
- 画面（Blazor）の自動テスト。画面の動作確認はブラウザで行う
- 価格の画面上での編集。価格を変えるときはmenu.jsonを書き換えて再ビルド・再デプロイする

## フォルダ・ファイル構成

```
/Users/yoshizawayuki/KFC/
├── KfcMenu.slnx                      ソリューション
├── KfcMenu.Core/                      クラスライブラリ（計算ロジック。画面に依存しない）
│   ├── KfcMenu.Core.csproj
│   ├── Models/
│   │   ├── Product.cs                 商品（チキン、ポテトSなど）
│   │   ├── MenuOption.cs              購入選択肢（4パック、セットなど）
│   │   ├── ChoiceSlot.cs              購入選択肢の「選べる枠」
│   │   ├── MenuData.cs                menu.json全体（products + options）
│   │   ├── Variant.cs                 選べる枠の選び方まで確定した購入選択肢
│   │   └── ComboResult.cs             計算結果
│   └── Services/
│       ├── MenuParser.cs              JSON文字列 → MenuData
│       ├── VariantExpander.cs         購入選択肢 → Variantの一覧に展開
│       └── CheapestComboFinder.cs     最安の組み合わせを探索
├── KfcMenu.Core.Tests/                xUnit（Coreのテスト。本物のmenu.jsonも読んで検証する）
├── KfcMenu.Web/                       Blazor WebAssembly（画面）
│   ├── KfcMenu.Web.csproj             KfcMenu.Coreを参照
│   ├── Program.cs
│   ├── Pages/Home.razor               入力・計算・結果表示の画面
│   └── wwwroot/
│       ├── index.html
│       └── data/menu.json             メニューデータ（サイトに同梱）
├── docs/superpowers/                  設計書・実装計画
├── KFCメニュー.numbers                 メニューデータの元ファイル
└── _archive/                          以前の構成（Web API雛形、コンソール版）。不要なら削除してよい
```

Coreを分ける理由: 計算ロジックが画面（Blazor）に依存しないので、ブラウザを開かずにテストで検証できる。

## データ

### menu.json（KfcMenu.Web/wwwroot/data/menu.json）

ユーザーが定義したスキーマ。ファイル先頭に `//` コメントがある。

- `products`: 商品の一覧（16件）
  - `id`: 商品ID（例: `chicken`, `potato_s`, `drink_s`）
  - `name`: 表示名
  - `required`: 必要数（画面の入力欄の初期値）
  - `MaxSurplus`: 余剰の上限個数（計算ロジックの入力。2026-10-24以降、画面はこの値を使わず常に0で最安を求める）
- `options`: 購入選択肢の一覧（30件）
  - `id`, `name`, `price`
  - `includes`: 必ず含まれる商品IDと個数（例: `{ "chicken": 4 }`）
  - `choices`（省略可）: 選べる枠の配列。各枠は `count`（選ぶ個数）, `from`（候補の商品ID）, `allowDuplicate`（同じ商品を重複して選べるか）, `label`（表示用）

価格と個数はKFCメニュー.numbersの値。numbersで「サイド」「ポテト」「ドリンク」とだけ書かれていた部分は、次のように仮定した（実際のメニューと違えばmenu.jsonを直す）:
- 4/6/8パックのサイド: 290円のサイド5種（ポテトS・クリスピー・チョコパイ・ビスケット・コールスロー）から選ぶ枠。重複可
- セット内のポテト: ポテトS固定（`includes`）
- セット内のドリンク: S・M・L・ホットから1つ選ぶ枠

### C#のモデル（KfcMenu.Core/Models）

- `Product`: `Id`, `Name`, `Required`, `MaxSurplus`（画面から書き換えるので set可能にする）
- `MenuOption`: `Id`, `Name`, `Price`, `Includes`（`Dictionary<string, int>`）, `Choices`（`List<ChoiceSlot>`、JSONで省略された場合は空として扱う）
- `ChoiceSlot`: `Count`, `From`（`List<string>`）, `AllowDuplicate`, `Label`
- `MenuData`: `Products`（`List<Product>`）, `Options`（`List<MenuOption>`）
- `Variant`: 元の `Option`（`MenuOption`）, 選んだ商品ID `Picks`（`List<string>`）, 中身の合計 `Contents`（`Dictionary<string, int>` = includes + picks）
- `ComboResult`: `Items`（`List<(Variant Variant, int Count)>`）, `TotalPrice`

### 読み込み

Web側が `HttpClient.GetStringAsync("data/menu.json")` で文字列を取得し、Coreの `MenuParser.Parse(string json)` に渡す。CoreはHttpClientに依存しない。

`System.Text.Json` のソース生成（`JsonSerializerContext`）を使う。Blazor WebAssemblyの発行時のトリミングで、リフレクション用の情報が消されて読み込みが壊れるのを避けるため。オプションは次の2つ:
- `PropertyNameCaseInsensitive = true`（JSONは `name`、C#は `Name`。`MaxSurplus` だけJSONでも大文字始まり）
- `ReadCommentHandling = JsonCommentHandling.Skip`（先頭の `//` コメントを読み飛ばす）

## アルゴリズム

2段階で解く。

### 1. 展開（VariantExpander）

購入選択肢ごとに、選べる枠の選び方をすべて列挙し、「中身が確定した購入選択肢（Variant）」に展開する。
- `allowDuplicate: true` の枠: 重複ありの組み合わせ（例: 5種から2つ → 15通り）
- `allowDuplicate: false` の枠: 重複なしの組み合わせ（例: 4種から1つ → 4通り）
- 枠がない購入選択肢: 1通り
- 枠が複数ある場合: 各枠の選び方の直積

現在のmenu.jsonでは30件 → 174件のVariantになる（4パック15、6パック35、8パック70、ドリンク付きセット各4、その他各1）。

### 2. 探索（CheapestComboFinder）

商品ごとに下限 `Required` と上限 `Required + MaxSurplus` を決め、どのVariantを何個買うかを決める。
- 事前の絞り込み: 中身のどれかが上限を超えるVariant（上限0の商品を含むものなど）は使えないので、最初に除外する
- 状態は「今の各商品の合計個数」。まだ下限に届いていない最初の商品を選ぶ
- 最も安い買い方では、どのVariantを1つ取り除いても下限を割る（割らないなら、取り除いた方が安い）。なので、足りない商品を含むVariantが必ず1つ以上入っている。それらだけを試し、上限を超えないものを1つ足して再帰する
- 同じ状態は何度も出てくるので、「その状態から下限を満たすまでの最小の追加金額」と「次に買うVariant」をメモしておく（メモ化）
- 全商品が下限に届いた状態は追加金額0。試せるVariantがなければ「不可能」
- 最初の状態（全部0個）の結果が答え。メモをたどって買うVariantを復元する。不可能なら `null`
- 同じ金額の候補が複数あるときは、どれを返してもよい（例: チキン10本は「10パック」と「単×10」がどちらも3300円）

最初の案（Variantを1つずつ0個から順に試すDFS）は、多くの商品を指定すると組み合わせが爆発するため採用しなかった。

現在のデータでは全商品に単品の購入選択肢があるため、上限が下限以上である限り「見つからない」は実際には起きない。それでもコード上は `null` を扱う。

## 画面（Home.razor）

- 起動時にmenu.jsonを読み込み、Variantに展開しておく。読み込み中は「読み込み中...」と表示する
- 商品ごとに1行: 商品名 / 必要数（数値入力）
- 「計算」ボタン → 結果エリアに表示。計算中は結果エリアに「計算中...」を表示し、「計算」ボタンを無効にする（計算は重いことがあるので、先に表示を更新してから探索を始める）
  - 見つかった場合: 各行「4パック × 1（サイド: クリスピー, クリスピー）」の形式と、合計金額
  - 見つからない場合: 「条件を満たす組み合わせが見つかりませんでした」
- 最安の下に「セットでおすすめ」を表示する（詳細は docs/superpowers/specs/2026-10-24-set-recommendation-design.md）
- 必要数の入力は0〜99に限る（`min="0" max="99"`）。範囲外のときは計算せず、「必要数には0〜99の数を入力してください。」と表示する
- スマホで見やすいように、1列の縦並びにする

## エラー処理

- menu.jsonの読み込み・パースに失敗した場合: 画面にエラーメッセージを表示する（例外で画面が真っ白にならないようにする）
- 0〜99の範囲外の入力（負の数・100以上）: 「必要数には0〜99の数を入力してください。」を表示し、計算しない。範囲を限るのは、極端に大きい数（例: 10000）で再帰が深くなりWebAssemblyのスタックが溢れ、画面が使えなくなるのを防ぐため
- 想定外の例外で出るBlazorのエラー表示（`#blazor-error-ui`）は日本語（「エラーが発生しました。」「再読み込み」）にする。ページの言語は `lang="ja"`

## 性能

計算はボタンを押したときにブラウザ内で同期的に行う。メモのキーは各商品の合計個数を混合基数で束ねた `long` で、商品ごとに「その商品を含むVariantの一覧」を先に作っておく。

計測結果（ブラウザはPC上のヘッドレスChrome、Release発行。WebAssemblyはPC上の.NETより10倍以上遅い）:

| 条件 | PC上の.NET 変更前 → 後 | WebAssembly 変更前 → 後 |
|---|---|---|
| H: チキン12・ポテトS6・ドリンクS6・ビスケット4・クリスピー3ほか、余剰1〜2（33.8万状態） | 660ms → 246ms | 約14.2秒 → 約0.88秒（レビュー担当の変更前の計測は14.5秒） |
| G: チキン30・ポテトS10・ドリンクS10・ビスケット9ほか、余剰あり | 7.9秒 → 0.85秒 | 変更前は未計測（10分待っても終わらなかった） → 約11.7秒 |
| 全16商品を必要数2・余剰の上限2 | 約22ms → 約3ms | 未計測 |

入力は必要数を0〜99に限る。上限を決めておけば、探索の深さと状態の数が有限に収まり、極端に大きい数でスタックが溢れることがない。

既知の制限: 複数の商品を多く、余剰ありで指定する重い条件（上のGのようなもの）は、PCのWebAssemblyでも十数秒、スマホではさらにかかることがある。この間は「計算中...」を表示し、「計算」ボタンを無効にする。これ以上の高速化は今回は行わない。（2026-10-24以降は画面から余剰を指定できないため、上の表のH・Gの条件は画面からは再現できない。おすすめ追加後の計測は docs/superpowers/specs/2026-10-24-set-recommendation-design.md の「性能」にある）

## テスト

- `KfcMenu.Core.Tests`（xUnit）で、MenuParser・VariantExpander・CheapestComboFinderをテストする
- CheapestComboFinderの期待値は、本物のmenu.jsonを使った別の総当たり計算と照合したもの
- テストは本物のmenu.jsonを読むので、価格を変えたらテストの期待値も直す
- 画面はブラウザで確認する（Release発行したサイトをサブパスで配信して確認する手順を実装計画に含める）

## ホスティング・デプロイ

- GitHub Pagesに置く。GitHub Actionsで `dotnet test` → `dotnet publish` → GitHub Pagesに公開する
- GitHub Pagesは `https://<ユーザー名>.github.io/<リポジトリ名>/` のようにサブパスで公開されるため、公開時に `index.html` の `<base href="/" />` をリポジトリ名入りに書き換える
- `.nojekyll` を置く（`_framework` フォルダがGitHub Pagesに無視されないようにするため）
- HTTPSはGitHub Pagesで標準で有効

## バージョン管理

現在このディレクトリはgitリポジトリではない。GitHub Pagesへのデプロイにはgitリポジトリが必要なので、デプロイのタスクで `git init` してGitHubにpushする（実施するかどうかはそのタスクの時点でユーザーが決める）。
