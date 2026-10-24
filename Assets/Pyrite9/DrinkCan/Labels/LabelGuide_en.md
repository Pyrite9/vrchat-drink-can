# Pyrite9 Drink Can — Making your own label

The label is one image wrapped once around the can's straight body. The top and bottom of the can (the lid, the shoulder, the bottom curve) are always aluminium.

## Quick steps

1. Select the **Drink Can** on your avatar and choose the **Size Preset** first (the template depends on it).
2. Press **Open template** in the inspector (or open the file from `Labels/` below) in your paint app.
3. Add a new layer over the template and draw on it. Use the template only as a guide.
4. Hide the template layer and export a **PNG at the same pixel size**.
5. Put the PNG in your own folder in the Project (for example `Assets/MyLabels/`), not inside `Pyrite9/DrinkCan`.
6. Drag it into **Label Texture**. Keep **Label Fit** on **Stretch**.
7. The first Play or upload after that may open the **NDMF** window with "Some of your textures have mip streaming disabled." — press **Auto Fix**.

Any paint app that can use layers works (CLIP STUDIO PAINT, Photoshop, Krita, GIMP, ibisPaint, ...).

## Templates

Each size template is 2048 px wide and has the body's exact proportions, so a drawing on it lands on the can without being stretched.

| Size Preset | Template | Pixels | Real size (around × height) | Ratio |
|---|---|---|---|---|
| 350 ml | `DrinkCan_LabelTemplate_350ml.png` | 2048 × 944 | 207.3 × 95.5 mm | 2.17 : 1 |
| 500 ml | `DrinkCan_LabelTemplate_500ml.png` | 2048 × 1396 | 207.3 × 141.5 mm | 1.47 : 1 |
| 250 ml slim | `DrinkCan_LabelTemplate_250mlSlim.png` | 2048 × 1456 | 166.5 × 118.2 mm | 1.41 : 1 |
| 190 g coffee | `DrinkCan_LabelTemplate_190gCoffee.png` | 2048 × 1096 | 166.5 × 89.2 mm | 1.87 : 1 |
| Custom | `DrinkCan_LabelTemplate.png` | 2048 × 1024 | — | 2 : 1 (see "Custom size") |

The grid is 10 mm (thin lines every 5 mm), measured from the front centre and from the bottom.

## Reading the template

- **50 % — FRONT** (light blue, about 30–70 %): the side that faces away from you while you drink, so this is what people around you see. Put the main logo or name here.
- **25 % / 75 %**: the sides. Good for the flavour, small text and patterns.
- **0 % and 100 % — SEAM** (pink, the outer 8 % on each side): the left and right edges of the image meet here, under the opening. Make the two edges continue into each other (same background colour or pattern), or put a deliberate vertical stripe there. Do not run text or a logo across the edge — it is cut in two.
- **Red lines at the top and bottom** (5 % of the height): there the body turns into the shoulder and the bottom curve. Keep text and important shapes between the red lines; let the background run to the edge.
- **Blue circle**: if it is round on the template, it is round on the can. Use it to check that nothing is squashed.
- Text reads normally on the can — do not mirror the image.

## Label Fit

| Setting | What it does | Use it for |
|---|---|---|
| **Stretch** (default) | The whole image covers the body, top to bottom and once around. | Labels drawn on the template |
| **Keep aspect** | The image goes once around; its height follows the image's own ratio. Leftover space above and below is bare aluminium. If the image is taller than the body, its top and bottom are cut off evenly. | A logo strip on a metal can, an image with a different ratio |
| **Repeat** | The image repeats around the can at full body height, as many times as keeps its proportions (at least once). | Small square patterns, stickers |

When the image's ratio does not match the body with Stretch, the inspector shows a note ("This body is 2.17:1, the label is 1.00:1 — Stretch distorts it noticeably").

## Custom size

With **Custom** height or diameter there is no exact template. Assign any image first; the inspector note tells you the body's ratio ("This body is X:1"). Make your image **2048 × (2048 ÷ X)** px — for example X = 1.80 gives 2048 × 1138 — and the note disappears. You can draw on the 2:1 template and resize the canvas to that height.

## Image tips

- **Size**: 2048 px wide is enough. Larger images are reduced to 2048 by Unity's default import setting. A height that is a multiple of 4 compresses best.
- **No transparency**: the label is opaque. Transparent pixels do not show the metal through — they show whatever colour those pixels still hold (often black or white). Fill the whole image. For a bare-metal look, use **Keep aspect** or paint a silver-grey.
- **Colours** look as in the image under neutral light; the world's lighting tints them like any other material.
- **Small text**: if fine text looks blurry, select the image in the Project and set **Compression** to **High Quality**, then **Apply**.
- **Format**: PNG is best. JPG works but leaves blocky edges around text.
- Real brands' logos are trademarks. Use your own designs (or the sample labels).

## Sample labels

`Labels/` also has finished labels you can look at or use, one per size (fictional brands): `DrinkCan_Label_WhiteStatic_500ml.png` · `DrinkCan_Label_RidgeDry_350ml.png` · `DrinkCan_Label_NightLimeCola_250mlSlim.png` · `DrinkCan_Label_DailyBean_190gCoffee.png`, and the prefab's default `DrinkCan_Label_PyriteLager.png`. Open one next to its template to see how the front, sides and seam are used.
