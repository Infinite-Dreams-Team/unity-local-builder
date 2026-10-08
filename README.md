# Local Builder (`pl.idreams.local-builder`)

Build Androida (AAB/APK) na lokalny dysk i kopiowanie podpisanych plików do docelowego katalogu (np. udziału sieciowego).
Gradle pracuje lokalnie. Na serwer trafiają tylko gotowe pliki: `.aab`/`.apk`, `.obb`, `*.symbols.zip` i opcjonalnie `<nazwa>_mapping.txt` z R8.

Pakiet ma własny asmdef (tylko Editor) i nie ma zależności.
Kod jest otoczony `#if UNITY_ANDROID`, więc menu i okno są dostępne tylko przy aktywnym targecie Android.

## Instalacja

Dodaj do `Packages/manifest.json`:

```json
"pl.idreams.local-builder": "https://github.com/Infinite-Dreams-Team/unity-local-builder.git#v1.0.0"
```

Bez dostępu do GitHuba: `npm pack` w tym folderze, skopiuj `.tgz` do `Packages/` projektu i dodaj jako
`"file:pl.idreams.local-builder-1.0.0.tgz"`.

Jeśli projekt ma starą kopię w `Assets/Plugins/LocalBuilder`, usuń ją przed dodaniem pakietu (te same klasy i asmdef).

## Użycie
- Menu **Builder**: `Build`, `Clean Build` (`BuildOptions.CleanBuildCache`), `Copy Last Build to Destination`, `Delete Local Build After Copy`, `Settings...`.
- Okno `Builder > Settings...` służy do ustawienia katalogu docelowego, podfolderu, nazwy pliku, katalogu lokalnego (domyślnie `Builds/Android`), opcji i haseł do keystore.
- Tokeny w podfolderze i nazwie: `{product} {company} {version} {version_} {code} {date} {time} {dev}`.
  Przykład: podfolder `{version}`, nazwa `game_{version_}_{code}` daje `1.03/game_1_03_82.aab`.
- Plik najpierw kopiuje się jako `*.partial`, a po zakończeniu kopiowania zmienia nazwę na docelową. Jeśli udział nie jest zamontowany, build zostaje lokalnie i można go skopiować później.
- **Delete local after copy** (domyślnie włączone, też jako przełącznik w menu Builder): po udanym kopiowaniu usuwa lokalne pliki builda.
  Plik jest usuwany tylko wtedy, gdy jego kopia w katalogu docelowym istnieje i ma ten sam rozmiar, i nie jest tym samym plikiem co lokalny.
  Jeśli kopiowanie się nie uda albo zostanie przerwane, lokalny build zostaje.
  Katalog lokalny powinien być przeznaczony tylko na buildy: do artefaktów trafia każdy plik zapisany w nim w trakcie builda.

Ustawienia są per użytkownik i zapisywane w `UserSettings/LocalBuilderSettings.asset`. Przy pierwszym uruchomieniu wypełniają się na podstawie ostatniej lokalizacji builda Androida.
Unity nie pamięta haseł keystore po restarcie. Opcja "Remember passwords" zapisuje je jawnym tekstem w EditorPrefs na tej maszynie.

## Linia komend
```
Unity -batchmode -quit -projectPath <projekt> -buildTarget Android \
  -executeMethod LocalBuilder.LocalBuild.BuildFromCommandLine [-lbClean] [-lbDest <katalog>] [-lbDevelopment]
```
Hasła pochodzą ze zmiennych `LB_KEYSTORE_PASS` / `LB_KEYALIAS_PASS` albo z zapamiętanych w EditorPrefs. Kod wyjścia to 0 (sukces) lub 1 (błąd).
