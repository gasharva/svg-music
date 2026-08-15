# SvgSymbols

Отдельный экспериментальный console-проект для набора разнообразных SVG-вариантов музыкальных ключей перед разработкой shape-эвристик.

Сейчас источник — Wikimedia Commons:

- `Category:G clef` — скрипичные/G-ключи;
- `Category:F clef` — басовые/F-ключи;
- по умолчанию также обходятся подкатегории на глубину 1.

Запуск из корня репозитория:

```powershell
dotnet run --project Experiments/SvgSymbols
```

Глубину обхода можно увеличить:

```powershell
dotnet run --project Experiments/SvgSymbols -- --depth 2
```

Результат:

```text
Experiments/SvgSymbols/
  Samples/
    Treble/
      *.svg
      sources.json
    Bass/
      *.svg
      sources.json
  gallery.html
```

`gallery.html` показывает все SVG плиткой. У каждого образца есть чекбокс `мусор / не подходит`; состояние сохраняется в `localStorage`, поэтому коллекцию можно спокойно просмотреть глазами в несколько заходов.

`Sources.json` хранит URL страницы Wikimedia Commons, URL оригинального SVG и лицензионные метаданные. Не стоит переносить образцы в постоянный test corpus без проверки конкретной лицензии и самого содержимого файла.

Следующий этап после ручной чистки — сравнивать геометрию оставшихся G/F-clef без привязки к их позиции на нотном стане.
