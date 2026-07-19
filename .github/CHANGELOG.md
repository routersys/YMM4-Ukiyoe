# v1.0.0 - 浮世絵 for YMM4

YukkuriMovieMaker4向けの浮世絵エフェクトプラグインの初回リリースです。
素材を多色木版画の原画とみなし、Kang・Lee・Chuiの「Coherent Line Drawing」（NPAR 2007）のエッジ接線流と流れに沿った差分ガウスで輪郭主線を求め、Bi・Han・Yuの「An L1 Image Transform for Edge-Preserving Smoothing and Scene-Level Intrinsic Decomposition」（SIGGRAPH 2015）のL1局所平坦化で地色を均し、Winnemöllerらの「Real-Time Video Abstraction」（SIGGRAPH 2006）の軟量子化で限定パレットへ寄せ、和紙の繊維とばれん目と版ずれを重ねて刷り上げます。
和紙の繊維とばれん目と版ずれの形はシードから決定論的に決まり、同じ設定では常に同じ絵になります。
計算はComputeSharpの計算シェーダーがDirect3D 12で実行し、YMM4のDirect3D 11側とは共有テクスチャおよび共有フェンスで接続します。
8言語のリソース構成のUIを備えます。

---

## 新機能

### 1. 浮世絵の計算パイプライン

`UkiyoePipeline`は、シルエット、構造、描画の3段階の計算シェーダーを`ComputeContext`へ記録して実行します。処理格子のバッファーは計算領域の大きさと品質に応じて確保し、サイズが変わらないフレームでは再利用します。処理の流れは次のとおりです。

1. `SilhouetteShader`が、各格子セルに対応する画素の色とアルファ値の平均を調べ、直色と白地の輝度を記録します。
2. `GradientShader`が、輝度のソーベル勾配から接線場の初期値と勾配強度を求めます。
3. `EtfShader`が、エッジ接線流の非線形ベクトル平滑化を反復します。
4. `FlattenShader`が、L1局所平坦化の反復重み付き最小二乗を反復し、色面を均します。
5. `DogShader`と`FlowAccumulateShader`が、勾配方向の1次元差分ガウスと流線方向の集計で主線の応答を求め、`SuperimposeShader`が黒画素を重ねて反復します。
6. `RenderShader`が、画素ごとに平坦化色を軟量子化し、版ずれ、ばれん目、和紙の繊維、主線を合成して出力します。

| シェーダー | 役割 |
|---|---|
| `SilhouetteShader` | 画素の色とアルファ値から直色と輝度を求める |
| `MaskHashShader` | 素材のハッシュと境界を集計する |
| `GradientShader` | ソーベル勾配と接線場の初期値を求める |
| `EtfShader` | エッジ接線流を平滑化する |
| `FlattenShader` | L1平坦化の1反復を解く |
| `CopyColorShader` | 平坦化の反復初期値を用意する |
| `DogShader` | 勾配方向の差分ガウスを求める |
| `FlowAccumulateShader` | 流線に沿って応答を集計する |
| `SuperimposeShader` | 主線を輝度へ重ねて反復する |
| `RenderShader` | 版画を描画する |

### 2. エッジ接線流と流れに沿った差分ガウス

輪郭主線は、Kang・Lee・Chuiの論文「Coherent Line Drawing」（NPAR 2007）に基づきます。

- エッジ接線流は、論文の式1〜5の核関数に基づく非線形ベクトル平滑化です。空間重みは半径5セルの円盤、強度重みは`(1 + tanh(ĝ(y) − ĝ(x))) / 2`、方向重みは`|t(x)・t(y)|`で、逆向きの接線は符号を反転してから加えます。勾配強度が最大値の2%を下回るセルは方向なしとして扱い、近傍の有意な接線の構造テンソルの主固有ベクトルで充填します。この充填により、素材の内部に境界と直交する緩い勾配があっても、シルエット境界の1〜2セル内側の接線が境界へ沿い、輪郭主線が途切れません。
- 差分ガウスは論文の式6〜9を2パスへ分離して求めます。第1パスは各セルの勾配方向へ1次元の`G_σc − 0.99・G_σs`（`σs = 1.6σc`）を畳み込み、第2パスは接線流の流線を両方向へ`⌈2σm⌉`ステップ辿り、`G_σm`の重みで応答を集計します。この分離は流線上の各点の応答を再利用する近似で、Kyprianidisらが実時間実装で用いた構成です。
- 二値化は式10の`1 + tanh(gain・H) < τ`で行い、描画では同じ式の連続値を幅0.1の軟しきい値として使い、主線の縁を滑らかにします。
- 論文3.2節の反復適用を行い、前回の主線の黒画素を輝度へ重ねてから再度差分ガウスを適用します。エッジ接線流は反復中に更新しません。

線の太さは中心ガウスの幅`σc`を0.6〜3.0画素、線の流れは流線ガウスの幅`σm`を1〜6セル、線の量はしきい値`τ`を0.1〜0.95へ割り当てます。

### 3. L1局所平坦化による地色の色面

地色は、Bi・Han・Yuの論文「An L1 Image Transform for Edge-Preserving Smoothing and Scene-Level Intrinsic Decomposition」（SIGGRAPH 2015）の局所平坦化項（式2）と画像近似項（式7）のエネルギーを最小化して均します。親和度は平滑化用の式9に従い、輝度を0.3倍へ抑えた色特徴の距離と、隣接セルの勾配の最大値の`η = 0.4`倍の大きいほうを使います。大域疎性項は論文の平滑化の節の記述どおり使用しません。

論文のSplit-Bregman法による大域の疎行列解法の代わりに、L1項を反復重み付き最小二乗として前向きに解きます。各反復は、近傍24セルについて重み`w_ij / max(|x_i − x_j|_1, ε)`を現在の反復値から求め、`x_i = (β・in_i + Σ w・x_j) / (β + Σ w)`のヤコビ更新を2枚のバッファーで交互に行います。更新は順序に依存しないため、GPU上で決定論的です。分解や推定を伴わない前向きの区分平坦化であり、常に均質な色面が得られます。平坦化のパラメータはデータ項の重み`β`を10⁵〜1の対数尺度で割り当てます。

### 4. 限定パレットと版ずれ・ばれん目・和紙

描画は`RenderShader`が画素ごとに行います。

- 階調化は、Winnemöllerらの「Real-Time Video Abstraction」（SIGGRAPH 2006）の輝度の軟量子化に基づきます。プラトーが階調の中心に来るように正規化した遷移幅0.18の軟階段で、暗さを色数の段数へ量子化します。彩度は平坦化とスケール0.9で整え、量子化しません。
- 版ずれは、階調の層ごとにシードから決めた方向へ最大10画素まで刷り位置をずらします。各画素で自身より暗い層のずらした標本を調べ、最も暗い層の色を採用するため、階調の境界に多色刷りの見当ずれの色縁が出ます。
- ばれん目は、セルごとに中心をずらした同心円模様を低周波の斑と掛け合わせ、中間調の暗さをわずかに明るくします。
- 和紙は、3オクターブの値ノイズの繊維場で暗さと主線を削り、かすれを作ります。

すべての模様はシードと画素位置のハッシュから決定論的に生成し、乱数と時間値を使用しません。出力はプリマルチプライドアルファで、各チャンネルをアルファ以下へクランプします。

### 5. 可視範囲への出力矩形の最小化

シルエットの集計時に、アルファ値を持つセルの境界を`InterlockedMin`と`InterlockedMax`で求め、8個の整数の読み戻しに含めます。表示矩形は素材の境界に主線の到達範囲と版ずれと余白を加えた大きさで、4画素境界へそろえます。出力テクスチャは矩形の大きさで確保し、Direct2Dの`Crop`と`AffineTransform2D`で元の位置へ合成します。画素の読み戻しは行いません。

### 6. 構造キャッシュ

輪郭と色面を決める入力が変わらないフレームでは、構造の計算を再利用します。

- 素材は、セル位置と量子化した色とアルファ値のハッシュの総和とXORの2値へ集約し、8個の整数の読み戻しで前フレームと比較します。
- 素材のハッシュ、計算領域の大きさ、品質、線の太さ、線の流れ、線の量、平坦化が一致する場合は、構造段階を実行しません。
- 構造が同じで、色数、版ずれ、ばれん目、和紙、線の濃さ、主線色、シード、出力矩形も変わらないフレームでは、描画段階も実行せず、前フレームの出力テクスチャを使用します。

### 7. Direct3D 11・Direct3D 12相互運用

`UkiyoeGpuInterop`は、YMM4のDirect3D 11・Direct2D側と、ComputeSharpのDirect3D 12側を接続します。ComputeSharpの`GraphicsDevice`は、YMM4が使うDXGIアダプターのLUIDと一致するものを選びます。

入力と出力は、ComputeSharpで確保した共有テクスチャをDirect3D 11のテクスチャとして開き、Direct2Dのビットマップとして扱います。両デバイスの同期は、Direct3D 12のフェンスを共有フェンスとしてDirect3D 11側で開いて行います。`BeginCompute`は、Direct3D 11のコマンドを送出したうえでDirect3D 12側を待機させ、`EndCompute`は、Direct3D 12側の完了をDirect3D 11側で待ちます。

Direct3D 12デバイスの取得や共有リソースの作成に失敗した場合は、`TryCreate`が`null`を返し、エフェクトを適用せず入力映像を表示します。

### 8. カスタムシェーダーによる合成

`UkiyoeCustomEffect`は、`[CustomEffect(2)]`の2入力エフェクトです。入力0は元映像、入力1は描画した版画です。ピクセルシェーダー`Ukiyoe.hlsl`の`main`は、`amount`が0以下のとき元映像をそのまま返し、そうでないときは版画のRGBをアルファでクランプし、`lerp(source, print, amount)`で元映像と混合します。版画の濃淡と模様は描画段階で画素へ焼き込むため、合成は単純な混合です。

定数バッファーは`Amount`と3つの詰め物で16バイトです。`MapInputRectsToOutputRect`は2つの入力矩形の和集合を出力矩形とします。主線と版ずれは素材の外側へわずかに広がるため、出力範囲は素材より大きくなります。

シェーダーリソース: `pack://application:,,,/Ukiyoe;component/Shaders/Ukiyoe.cso`（ps_5_0、`ShaderResourceUri.Get`が生成）

### 9. エフェクト定義とパラメータ

`UkiyoeEffect`は、YMM4の映像エフェクトとして宣言されます。

`[VideoEffect]`属性は以下のパラメーターで宣言されます。

- 表示名: `Texts.Ukiyoe`（ローカライズキー、日本語では「浮世絵」）
- カテゴリー: `VideoEffectCategories.Filtering`・`VideoEffectCategories.Decoration`
- 検索タグ: `TagWoodblock`・`TagPrint`・`TagJapanese`
- `IsAviUtlSupported = false`によりAviUtl向けEXO出力は非対応
- `ResourceType = typeof(Texts)`でローカライズリソースを指定

公開プロパティは以下のとおりです。基本項目は「基本」グループ、主線項目は「主線」グループ、色面項目は「色面」グループ、意匠項目は「意匠」グループに属します。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 | あり |
| `Quality` | `UkiyoeQuality` | `High` | — | なし |
| `LineWidth` | `Animation` | 50 | 0〜100 | あり |
| `Coherence` | `Animation` | 50 | 0〜100 | あり |
| `LineDetail` | `Animation` | 50 | 0〜100 | あり |
| `LineStrength` | `Animation` | 85 | 0〜100 | あり |
| `LineColor` | `Color` | #FF1E1A18 | — | なし |
| `Flatten` | `Animation` | 60 | 0〜100 | あり |
| `PaletteLevels` | `int` | 6 | 2〜16 | なし |
| `Misregistration` | `Animation` | 30 | 0〜100 | あり |
| `Baren` | `Animation` | 40 | 0〜100 | あり |
| `Paper` | `Animation` | 50 | 0〜100 | あり |
| `Seed` | `int` | 0 | 0〜int.MaxValue | なし |

`GetAnimatables`は`Amount`・`LineWidth`・`Coherence`・`LineDetail`・`LineStrength`・`Flatten`・`Misregistration`・`Baren`・`Paper`を返します。`Seed`は負値を代入すると0へ丸め、`PaletteLevels`は2〜16へ丸めます。

`CreateExoVideoFilters`は空のシーケンスを返します（EXO非対応）。`CreateVideoEffect`は映像処理用のインスタンスを生成します。エフェクトを最初に生成したときに、更新確認を一度だけ開始します。

### 10. フレームごとの更新

各フレームでYMM4の`EffectDescription`からフレーム位置、アイテム長、FPSを取得し、アニメーション値を評価します。値をパイプラインが前提とする範囲へ制限してから転送します。

| パラメータ | 変換 |
|---|---|
| `Amount` | `value / 100` をカスタムシェーダーの`Amount`へ |
| `LineWidth` | `value / 100` を0〜1へクランプし、中心ガウスの幅0.6〜3.0画素へ |
| `Coherence` | `value / 100` を0〜1へクランプし、流線ガウスの幅1〜6セルへ |
| `LineDetail` | `value / 100` を0〜1へクランプし、二値化のしきい値0.1〜0.95へ |
| `LineStrength` | `value / 100` を0〜1へクランプ |
| `Flatten` | `value / 100` を0〜1へクランプし、データ項の重み10⁵〜1へ |
| `PaletteLevels` | 2〜16へクランプ |
| `Misregistration` | `value / 100` を0〜1へクランプし、刷りずれ0〜10画素へ |
| `Baren` | `value / 100` を0〜1へクランプ |
| `Paper` | `value / 100` を0〜1へクランプ |
| `LineColor` | RGB各成分を0〜1へ |
| `Seed` | 0以上へクランプ |

強さが0以下のときは入力映像をそのまま出力します。入力の範囲が有限でない場合や、計算領域の余白を確保できない場合も、入力映像を表示します。

### 11. 品質設定

品質は、処理格子の解像度と各段階の反復回数をまとめて切り替えます。

| 品質 | 格子解像度 | 接線流反復 | 主線反復 | 平坦化反復 |
|---|---:|---:|---:|---:|
| 標準 | 1024 | 2回 | 2回 | 24回 |
| 高品質 | 1440 | 3回 | 2回 | 40回 |
| 最高品質 | 2048 | 3回 | 3回 | 64回 |

格子解像度は計算領域の長辺のセル数です。短辺のセル数は計算領域の縦横比に合わせ、最小4セルとします。

### 12. ローカライズ

`Texts`クラスは`[AutoGenLocalizer]`属性を持つ`partial`クラスとして宣言されます。
`YukkuriMovieMaker.Generator`のソースジェネレーターが`Texts.csv`を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース: 日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

主なローカライズキーは以下のとおりです。

| キー | ja-jp |
|---|---|
| `Ukiyoe` | 浮世絵 |
| `BasicGroup` | 基本 |
| `LineGroup` | 主線 |
| `ColorGroup` | 色面 |
| `CraftGroup` | 意匠 |
| `Amount` | 強さ |
| `Quality` | 品質 |
| `LineWidth` | 線の太さ |
| `Coherence` | 線の流れ |
| `LineDetail` | 線の量 |
| `LineStrength` | 線の濃さ |
| `LineColor` | 主線色 |
| `Flatten` | 平坦化 |
| `PaletteLevels` | 色数 |
| `Misregistration` | 版ずれ |
| `Baren` | ばれん目 |
| `Paper` | 和紙 |
| `Seed` | シード |
| `QualityBalanced` | 標準 |
| `QualityHigh` | 高品質 |
| `QualityUltra` | 最高品質 |
| `TagWoodblock` | 木版画 |
| `TagPrint` | 版画 |
| `TagJapanese` | 和風 |
| `UpdateAvailableMessage` | 新しいバージョン {0} が公開されています。 |
