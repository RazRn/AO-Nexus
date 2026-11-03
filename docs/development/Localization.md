# Localization

English and Danish text is stored in `src/Common/Resources/Strings.en.xml` and `Strings.da.xml`. Both the launcher and engine embed the same catalogs. UTF-8 XML preserves Unicode, spaces, line breaks and format placeholders.

Use `Texts.Get("resource_key")` for new messages. Keys produced by the migration contain a readable prefix and a disambiguating suffix; retain a key when editing wording. New keys should describe the feature and purpose, such as `Option_fastStartup_Help`.

`Texts.T(english)` and `Texts.F(english, values)` are compatibility adapters for older callers and controls. Their translations come from the same resources using the English entry's `legacy="true"` alias. Do not add inline English/Danish pairs or another translation dictionary.

Add the same key to every shipped catalog. Preserve numbered format arguments. The unit suite verifies key parity, values and placeholder parity. Missing keys fail explicitly; missing translations in another language fall back to English.

To add a language, add a catalog and embed it in the launcher, engine and tests with logical name `AOLauncher.Strings.<language>.xml`, then expose it in the language selector. Texts discovers embedded languages and resolves an exact locale or its base language, falling back to English.

`tools/MigrateResources` records the one-time Roslyn-based migration of literal pairs and dictionaries. It intentionally refuses to overwrite existing catalogs; normal translation maintenance edits the resource files directly.
