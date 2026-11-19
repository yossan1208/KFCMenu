# 必要数の＋−ボタン Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 指で操作する端末（スマホ・タブレット）でだけ、必要数の入力欄の左右に「−」「＋」ボタンを出し、1ずつ増減できるようにする。

**Architecture:** 画面（`Home.razor`）に増減の処理とボタンを足し、ボタンを出すかどうかは CSS のメディアクエリ `(hover: none) and (pointer: coarse)` だけで決める。計算ロジック（`KfcMenu.Core`）は変えない。

**Tech Stack:** .NET 10, Blazor WebAssembly, CSS

**Spec:** `docs/superpowers/specs/2026-11-19-stepper-buttons-design.md`

## Global Constraints

- 必要数の範囲は 0〜99（今の入力チェック「必要数には0〜99の数を入力してください。」と同じ値）
- ボタンを出す条件: `@media (hover: none) and (pointer: coarse)`。それ以外ではボタンを出さず、PCの見た目は今と同じ
- 入力欄は残し、直接入力もできる
- ボタンの読み上げ名: 「{商品名}を1つ減らす」「{商品名}を1つ増やす」
- ボタンの大きさは 44px 四方程度。375px 幅で商品名・ボタン・入力欄が1行に収まる
- `Home.razor` と `app.css` は CRLF のまま保つ
- 計算ロジック（`KfcMenu.Core`）と menu.json は変更しない。既存のテスト 61 件はそのまま通ること

## Review Focus

1. 数字を打ち込んだ直後（入力欄にフォーカスが残ったまま）に「＋」を押す: 打った数に 1 足した値になること（例: 5 と打って＋ → 6）。Blazor は change → click の順に処理するはず。Task 1 のブラウザ確認で確かめる
2. スマホで「＋」を素早く連打する: ダブルタップでの画面拡大が起きず、押した回数だけ増えること → ボタンに `touch-action: manipulation`
3. 範囲外の数（150）が入っているときの「−」: 99 になり、「＋」は押せないこと（仕様の例）
4. 計算中: ボタンが押せないこと（計算ボタン・時間帯の切り替えと同じ `calculating`）
5. 長い商品名（ホットドリンク、骨なしチキン）でも 375px 幅で折り返したりはみ出したりしないこと

---

### Task 1: ＋−ボタンの追加

**Files:**
- Modify: `KfcMenu.Web/Pages/Home.razor`（30行目の入力欄、`@code` の 0〜99 チェック付近）
- Modify: `KfcMenu.Web/wwwroot/css/app.css`（`.product-row` / `.product-row input` 付近、末尾にメディアクエリ）
- Modify: `docs/superpowers/specs/2026-11-19-stepper-buttons-design.md`（確認結果を書くのみ、必要なら）

**Interfaces:**
- Consumes: `Product.Required`（int, 読み書き可）、`calculating`（bool）
- Produces: `private const int MaxRequired = 99;`、`private void Step(Product product, int delta)`

自動テストの対象（`KfcMenu.Core`）は変わらないので、このタスクの確認はブラウザで行う。

- [ ] **Step 1: 既存のビルドとテストが通ることを確かめる**

Run: `dotnet build KfcMenu.slnx --no-incremental && dotnet test KfcMenu.slnx`
Expected: 警告 0、エラー 0、61 件合格

- [ ] **Step 2: `Home.razor` に定数と増減の処理を足す**

- `private const int MaxRequired = 99;` を足し、今の `p.Required is < 0 or > 99` と入力欄の `max="99"` をこの定数に置き換える（エラーメッセージの文言はそのまま）
- `private void Step(Product product, int delta)`: `product.Required = Math.Clamp(product.Required + delta, 0, MaxRequired)`。計算中は何もしない。結果は消さない

- [ ] **Step 3: 入力欄の左右にボタンを置く**

入力欄を `<div class="qty">` で包み、その中に「−」ボタン、入力欄、「＋」ボタンの順に並べる。

- 「−」: `type="button"`、`class="step"`、`aria-label="{商品名}を1つ減らす"`、`disabled="@(calculating || product.Required <= 0)"`、押すと `Step(product, -1)`。表示文字は「−」
- 「＋」: 同様に `aria-label="{商品名}を1つ増やす"`、`disabled="@(calculating || product.Required >= MaxRequired)"`、押すと `Step(product, 1)`。表示文字は「＋」
- 入力欄の属性（`aria-label`、`@bind`、`inputmode` など）は変えない

- [ ] **Step 4: CSS を足す**

- ふだんは `.step { display: none; }`。`.qty` の中の入力欄は今と同じ幅（列幅 4.5rem）
- `@media (hover: none) and (pointer: coarse)` の中で:
  - `.product-row` の右の列を `auto` にする
  - `.qty` を横並び（flex、隙間 0.3rem 程度、縦中央）
  - `.step` を表示: 44px 四方、文字 1.3rem 程度、枠と文字は時間帯の切り替えと同じ赤（`#c8102e`）、背景白、角丸 0.5rem、`touch-action: manipulation`、押せないときは `opacity: 0.4`
  - 入力欄は幅 3rem 程度、中央揃え
- メディアクエリは `app.css` の末尾にまとめる

- [ ] **Step 5: ビルドとテスト**

Run: `dotnet build KfcMenu.slnx --no-incremental && dotnet test KfcMenu.slnx`
Expected: 警告 0、エラー 0、61 件合格

- [ ] **Step 6: ブラウザで確認する（PC）**

`dotnet run --project KfcMenu.Web --launch-profile http`（http://localhost:5127）を開く。

Expected:
- `matchMedia('(hover: none) and (pointer: coarse)').matches` が false
- 「−」「＋」が見えない（`getComputedStyle` で `display: none`）。見た目が変更前と同じ
- チキンに 2 を打って計算 → 最安 660円（通常）または 660円（ランチ）が出る

- [ ] **Step 7: ブラウザで確認する（スマホ）**

ブラウザをスマホとして表示する（Claude のブラウザ画面なら `resize_window` の `mobile`。そこで `matchMedia('(hover: none) and (pointer: coarse)').matches` が true にならなければ、Chrome の DevTools プロトコルでタッチを有効にした幅 375px の画面を使う。`.superpowers/sdd/2026-10-30-lunch-menu-v2/tools/shot.mjs` の接続部分を流用できる）。

Expected:
- matchMedia が true で、16 商品すべてに「−」「＋」が見える。375px 幅で 1 行に収まり、横スクロールがない
- チキンで「＋」を 3 回 → 3。「−」を 1 回 → 2。0 で「−」が押せない
- 入力欄に 5 と打ち、フォーカスを残したまま「＋」→ 6（Review Focus 1）
- 入力欄に 99 → 「＋」が押せない。150 と打って「−」→ 99（Review Focus 3）
- チキン 2 で計算 → PC と同じ結果
- ボタンの `aria-label` が「チキンを1つ増やす」など
- スクリーンショットを 1 枚撮って見た目を確かめる

- [ ] **Step 8: コミット**

```bash
git add KfcMenu.Web/Pages/Home.razor KfcMenu.Web/wwwroot/css/app.css
git commit -m "Add +/- buttons to the required counts on touch devices"
```

---

## リリース（ユーザーの確認を取りながら行う）

1. `stepper-buttons` を push し、PR を作る（`gh pr create`）
2. ユーザーがマージしたら、手元の main を更新し、公開の処理が成功したことと、公開ページで（スマホ表示で）ボタンが動くことを確かめる
3. タグを付けるかはユーザーに聞く（例: `v2.1`）
