# Whale Deck identity branding

Native Authentik Brand appearance assets. Authentication flows, providers,
groups, policy bindings and application launch URLs are not managed here.
The upstream "Powered by authentik" attribution remains visible.

## Applying

`python3 deploy/install/sync-authentik-branding.py` inspects without writes.
Add `--apply` to upload content-addressed media and update only the default
Brand's five presentation fields. The token is read privately from the running
API or an installer private environment file. Never pass it on the command line.
Run twice: the second run must report no changed fields or duplicate media.

For a host-specific original wallpaper, add `--background-file /absolute/private/image.jpg`
(JPG/PNG/WebP). The original bytes are uploaded as content-addressed native media,
not re-encoded. Future default synchronizations preserve that selected wallpaper;
they do not replace it with the bundled fallback. Keep third-party user images
outside Git (for example in ignored `deploy/artifacts/`); do not publish them as
part of the public source repository. A preserved background has a null returned
`backgroundSha256`: no new full-byte background verification is claimed by that
run; independently verify the actual anonymous login background when accepting it.

Media values are relative filenames resolved by Authentik; temporary signed
download URLs are never persisted in CSS. Assets survive container replacement
in Authentik's existing persistent data mount. No Whale Deck asset server is
required. Keep this directory when packaging the installer.

## Assets and generation

- `whaledeck-mark.png`: unchanged copy of the existing Whale Deck transparent mark
  from `web/public/images/whaledeck-mark.png`.
- `wordmark.svg.template`: native mark embedded with a cropped SVG viewport and
  theme-blue Whale Deck wordmark. No raster re-editing.
- `whaledeck-ocean-v1.png`: original 1672 × 941 PNG generated on 2026-10-20 with
  the built-in image generation tool (native mode), not an external CLI. It is
  the default fallback background for new installations. An existing host-specific
  selected wallpaper is preserved. No post-generation raster editing.
- `whaledeck.css`: native Brand custom CSS, scoped flow parts/stage hosts,
  light/dark surfaces, responsive card, keyboard focus, reduced motion; no JS,
  remote fonts, heavyweight blur or replacement authentication forms.

Full image prompt:

> Use case: stylized-concept. Asset type: original panoramic login background for Whale Deck, a blue-themed self-hosted workstation dashboard using Authentik. Create a refined editorial 3D/painted ocean-at-dawn scene: a single graceful humpback whale swimming beneath a translucent deep-navy-to-cobalt ocean surface, soft cyan shafts of light and delicate suspended particles, a quiet luminous wave near the upper left, calm secure technical atmosphere, no UI, no servers, no cubes, no typography, no logos, no watermark. Anatomically coherent whale with a single tail fluke and only the natural pair of pectoral fins, seen in subtle distant silhouette occupying the left third; no extra fins or fantastical appendages. Wide 16:9 composition, premium restrained blue palette, generous low-detail dark blue negative space in the center/right to keep a login form readable. The ocean must feel elegant and welcoming, not scary, not neon, no busy objects or harsh bloom. A polished production background usable behind both translucent dark and light login cards.

Official references: [native Brand customization](https://docs.goauthentik.io/customize/branding/),
[media picker](https://next.goauthentik.io/customize/file-picker/).
