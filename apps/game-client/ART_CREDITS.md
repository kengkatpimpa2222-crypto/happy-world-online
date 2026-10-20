# Art & audio credits

Everything under `Assets/_Project/Resources/Art/` may be used commercially. Sources:

## Kenney (www.kenney.nl), CC0 1.0 public domain

Taken from Kenney's official starter kits on GitHub (https://github.com/KenneyNL). The kits' README files state that
the bundled 2D sprites, 3D models and sound effects are CC0
(https://creativecommons.org/publicdomain/zero/1.0/); the kits' *code* is MIT and none of it is used here.
Crediting Kenney is not required but we do it gladly.

| File(s) in Resources/Art | Kit | Original file |
|---|---|---|
| Models/city/building-small-a/b/c/d, building-garage, grass-trees, grass-trees-tall, pavement-fountain (.bytes = .glb), colormap.png | Starter-Kit-City-Builder | models/*.glb, colormap.png |
| Models/city/character, coin, flag, cloud, bush (= grass), bush-small (= grass-small) | Starter-Kit-3D-Platformer | models/*.glb |
| Models/arena/tree, colormap.png | Starter-Kit-Basic-Scene (Mini Arena 1.0, credit also Tony Schär) | sample/Mini Arena |
| Audio/sfx_coin | Starter-Kit-3D-Platformer | sounds/coin.ogg |
| Audio/sfx_pop | Starter-Kit-3D-Platformer | sounds/jump.ogg |
| Audio/sfx_click, sfx_miss, amb_city | Starter-Kit-City-Builder | sounds/placement-a.ogg, removal-a.ogg, ambience.ogg (re-encoded mono) |
| Audio/sfx_good, sfx_whoosh, sfx_tap | Starter-Kit-Match-3 | sounds/tile-match.ogg, tile-swap.ogg, tile-land.ogg |

The Godot/Kenney logo tiles in those kits are **not** used.

## VRoid Studio sample models (pixiv Inc.), CC0 1.0 public domain

The anime player avatars in `Resources/Art/Models/avatar/` are made from the VRoid Studio beta sample models,
which pixiv released under CC0 (https://vroid.pixiv.help/hc/en-us/articles/4402614652569). Every source file
also says so in its own VRM meta (`licenseName: CC0`, `commercialUssageName: Allow`); `Tools/art/build_avatars.py`
refuses any file that doesn't. Files were taken from https://github.com/madjin/vrm-samples (`vroid/beta`).

| Avatar part | Sample model |
|---|---|
| female skeleton + face, `school_uniform` outfit, `long_black` hair | Sendagaya Shino |
| female `casual` outfit, `short_brown` hair | Vivi |
| female `basic_tee` outfit | HairSample_Female |
| female `thai_silk` outfit | Darkness Shibu |
| female `star_idol` outfit | Victoria Rubin |
| female `bob_black` hair | Sendagaya Shibu |
| male skeleton + face, `school_uniform` outfit, `short_brown` hair; `thai_silk` = the uniform recoloured | Sakurada Fumiriya |
| male `casual` outfit, `messy_black` hair; `star_idol` = the hoodie recoloured | HairSample_Male |

What the script changed: parts refitted onto one skeleton per body type, skin hidden under clothes removed,
triangles reduced (about 14-16k per character), textures shrunk into atlases, recolours as noted. The straw hat
(`*_hat_straw`, `hat_straw.png`) is made by the script. Rebuild with:

```bash
pip install numpy pillow meshoptimizer
git clone --depth 1 --filter=blob:none --sparse https://github.com/madjin/vrm-samples /tmp/vrm-samples
git -C /tmp/vrm-samples sparse-checkout set --no-cone 'vroid/beta/*.vrm'
python3 apps/game-client/Tools/art/build_avatars.py /tmp/vrm-samples/vroid/beta
```

## Made for this project (same licence as the game)

- Music and generated sounds: music_town_day, music_town_night, music_minigame, amb_rain, sfx_fanfare, sfx_star,
  sfx_lose, sfx_countdown, sfx_go, loop_sizzle, loop_pour. Composed and synthesised from code by
  `Tools/audio/compose.py` (numpy + ffmpeg), no samples.
- Mini-game sprites in `Resources/Art/Minigames/*.png`: drawn from code by `Tools/art/draw_sprites.py` (Pillow).
- Ground textures and rounded UI panels are generated at runtime (CityBuilder.GroundMat, UiKit.Rounded).

Re-run the scripts to regenerate:

```bash
python3 apps/game-client/Tools/audio/compose.py   # needs numpy and ffmpeg
python3 apps/game-client/Tools/art/draw_sprites.py # needs Pillow
```
