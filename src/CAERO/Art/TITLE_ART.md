# CAERO タイトル画像

生成: 組み込み image_gen ツール（imagegen スキルの built-in mode）。新規生成、透過なし。生成原本は `title-source.png`、ゲーム用は `../Content/title.png`（1440×900）。生成結果を既定の描画サイズへ高品質リサンプルして同梱した。

ロゴ「CAERO」は画像内に含める。日本語のメニューはゲーム側で重ね、キーボード・マウスの操作と解像度変更に対応する。拡大縮小時には縦横比を保ち、画面を覆うように中央を基準に描画する。

使用した最終プロンプト:

> Create a finished atmospheric title-screen background image for the exploration game CAERO. Use case: stylized-concept. Output exactly 1440 by 900 pixels (16:10), matching the game's default window drawing size. A quiet, mysterious maze of weathered pale stone towers, skeletal tall buildings with external zigzag stairs, thin elevated stone walkways crossing between islands above a fog-filled sea; muted desaturated blue-gray, moss and ivory stone; distant towers dissolve into soft luminous morning mist. Painterly 3D game key art, restrained and contemplative, architectural depth, no people. Composition: architecture toward left and right edges, a distant central vanishing point; upper central area simple light fog for the title; lower center dark muted mist with unobtrusive details so four menu buttons can be overlaid at 55%-90% image height. Only text in the image is the exact word 'CAERO' in large elegant widely spaced geometric ivory lettering, horizontally centered near 23% of image height. Absolutely no numeral 5, no subtitle, no menu text, no UI buttons, no watermark. Keep the architecture faithful to a low-poly stone sky-corridor exploration world, no futuristic metallic skyscrapers. Save the generated file so it can be copied into the game project.
