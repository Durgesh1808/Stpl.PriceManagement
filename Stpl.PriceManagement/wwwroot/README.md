# wwwroot: static files, by area

The same split as `Areas/` and the database schemas: what every screen uses
sits in `common/`, and what belongs to one pricing type sits in a folder named
after that area.

```
wwwroot/
  common/                  shared by every area
    css/app.css            the one stylesheet every page loads
    js/site.js             behaviour every page uses (bell, menus, buttons)
    images/                logo, favicon, login background, empty-state pictures...
  intlgit/                 International GIT pricing only
    js/workspace.js        the price revision grid
    images/                pictures only GIT screens show
  (intlfit/, domestic/ ... a future area gets its own folder the same way)
```

## Rule of thumb

- Used by more than one area, or by the layout / login / notifications -> `common/`
- Used only by one area's screens -> that area's folder
- If an area image later becomes shared, move it to `common/images/` and update its links

## Using them in a view

```cshtml
<img src="~/common/images/logo.png" alt="Southern Travels" asp-append-version="true" />
<img src="~/intlgit/images/europe.jpg" alt="" asp-append-version="true" />
```

`~/` is the site root. `asp-append-version="true"` adds a version stamp, so
browsers pick up a changed image instead of showing the cached old one.

An area stylesheet, when one is needed (`wwwroot/intlgit/css/intlgit.css`),
is added from the area's view:

```cshtml
@section Styles {
    <link rel="stylesheet" href="~/intlgit/css/intlgit.css" asp-append-version="true" />
}
```

From CSS, reference images relative to the stylesheet, for example in
`common/css/app.css`: `background-image: url("../images/login-bg.jpg");`

## Naming

Lower-case, words joined with hyphens, saying what the picture is:
`logo.svg`, `logo-white.svg`, `favicon.ico`, `login-background.jpg`,
`empty-change-sets.svg`. Prefer SVG for logos and icons, and WebP or
compressed JPG for photos.
