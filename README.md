# LMK Unity Packages

Editor tools for Unity, published as Unity Package Manager (UPM) packages.

| Package | Description | Branch | Latest |
|---|---|---|---|
| [`com.lmk.attributes`](../../tree/upm/com.lmk.attributes) | Inspector attributes: `[InlineInstanced]` searchable type picker for `[SerializeReference]` fields, lists and arrays. | `upm/com.lmk.attributes` | 0.2.0 |
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
    "com.lmk.attributes": "0.2.0"
  }
  ```

### Git URL

Add each package in *Package Manager → + → Add package from git URL*, dependencies first. Git URLs don't resolve dependencies automatically.

```
https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.1.1
https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.attributes/0.2.0
```

## Package signatures

New releases are signed with [Unity package signatures](https://docs.unity3d.com/Manual/upm-signature.html). Each signed tarball is attached to the version's GitHub Release, and OpenUPM serves that tarball.

- The Package Manager shows LMK packages as **Signed (limited)**, because the signing organization isn't one of yours. This is expected.
- Unity 6.7 and newer block packages signed by other organizations by default. To install LMK packages, set *Project Settings → Package Manager* security threshold to **Accept all signed packages**.
- Unity doesn't check signatures on Git URL installs.
- On OpenUPM, versions published before signing started (`com.lmk.attributes` 0.1.0, `com.lmk.editor` 0.1.1 and earlier) are unsigned.

## Repository layout

- Each package lives on its own branch, `upm/<package>`, with the package at the branch root.
- Each version is a tag `<package>/<version>`, e.g. `com.lmk.attributes/0.1.0`.
- Each signed version has a GitHub Release with the same tag and the signed `.tgz`.
- This branch (`main`) holds this page and the signing workflow.

Documentation, changelog and licence are in each package branch.

## License

[MIT](LICENSE.md)
