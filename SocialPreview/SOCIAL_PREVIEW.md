# Social preview

`social-preview.png` は CAERO のリポジトリ共有用画像です（1280×640、PNG）。
GitHub リポジトリの Settings → General → Social preview でアップロードして使用します。
リポジトリへの画像追加だけでは GitHub の設定には反映されません。

既存の `src/CAERO/Art/title-source.png` を参考に、組み込み image_gen ツールで生成しました。
生成画像を縦横比を維持して 1280×640 に高品質リサンプルしています。
ゲーム画面のスクリーンショットではなく、プロモーション用のアートです。

使用した最終プロンプト:

```text
Use case: ads-marketing.
Asset type: finished GitHub repository social preview for the game CAERO. Wide landscape canvas, exact aspect ratio 2:1, ideally 1280 by 640 pixels.
Input image: reference image for the existing CAERO visual identity, architecture, typography and palette.
Create a polished standalone promotional cover based on this title artwork, recomposing it for a 2:1 landscape social card. Weathered pale limestone towers and narrow arched bridges with exterior zigzag stairs on islands above a fog-covered sea, distant islands fading into luminous blue-gray morning mist. Restrained painterly 3D game key art, quiet mysterious contemplative atmosphere, muted ivory and blue-gray, subtle moss. Tall architecture frames both sides, a distant central vanishing point and a small hint of warm dawn light. Keep the same elegant thin widely spaced ivory CAERO lettering, large and clearly legible centered in the upper-middle, with the exact subtitle "THE SILENT ARCHIPELAGO" beneath it in smaller widely spaced uppercase lettering. The title and subtitle must stay well within central 80% of the canvas with generous margins and adequate contrast. The architecture and mist fill the whole image. Only these two text lines, no version number, no menus, no buttons, no badges, no watermark, no people. This is illustrated promotional artwork rather than an actual gameplay screenshot. Preserve the distinctive A styling in the reference.
```
