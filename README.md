# LMK Unity Packages

Editor tools for Unity, published as Unity Package Manager (UPM) packages.

| Package | Description | Branch | Latest |
|---|---|---|---|
| [`com.lmk.attributes`](../../tree/upm/com.lmk.attributes) | Inspector attributes: `[InlineInstanced]` searchable type picker for `[SerializeReference]` fields, lists and arrays. | `upm/com.lmk.attributes` | 0.1.0 |
| [`com.lmk.editor`](../../tree/upm/com.lmk.editor) | Shared UI Toolkit editor library: theme, searchable hierarchical dropdown, search syntax, controls. | `upm/com.lmk.editor` | 0.1.1 |

Requires Unity 6000.3 or newer.

## Install

### OpenUPM (recommended)

Dependencies between LMK packages are installed automatically.

- With [openupm-cli](https://openupm.com/docs/getting-started-cli.html), in your project folder:
  ```
  openupm add com.lmk.attributes
  ```
- Or add to `Packages/manifest.json`:
  ```json
  "scopedRegistries": [
    { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "com.lmk" ] }
  ],
  "dependencies": {
    "com.lmk.attributes": "0.1.0"
  }
  ```

### Git URL

Add each package in *Package Manager → + → Add package from git URL*, dependencies first. Git URLs don't resolve dependencies automatically.

```
https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.1.1
https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.attributes/0.1.0
```

## Repository layout

- Each package lives on its own branch, `upm/<package>`, with the package at the branch root.
- Each version is a tag `<package>/<version>`, e.g. `com.lmk.attributes/0.1.0`.
- This branch (`main`) only holds this page.

Documentation, changelog and licence are in each package branch.

## License

[MIT](LICENSE.md)
