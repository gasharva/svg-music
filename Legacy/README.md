# SVG → MusicXML PoC v4

🎼 **Latest Yellow Leaves:** [MusicXML](https://raw.githubusercontent.com/gasharva/svg-music/ci-output/latest/yellow-leaves.musicxml) · [analysis](https://raw.githubusercontent.com/gasharva/svg-music/ci-output/latest/yellow-leaves.analysis.json) · [classification](https://raw.githubusercontent.com/gasharva/svg-music/ci-output/latest/yellow-leaves.classification.json) · [performance](https://raw.githubusercontent.com/gasharva/svg-music/ci-output/latest/yellow-leaves.performance.json) · [source PR](https://raw.githubusercontent.com/gasharva/svg-music/ci-output/latest/source.txt)

Векторный конвертер нотного SVG в MusicXML без промежуточной растеризации.

## Главный сценарий

```powershell
dotnet run -- convert Samples\score_5.svg References\catalog.json score.musicxml
```

`convert` выполняет весь конвейер:

1. находит нотные станы;
2. извлекает единый поток глифов из `<use>` и самостоятельных `<path>`;
3. классифицирует формы по каталогу Bravura/SMuFL;
4. распознаёт головки нот и их высоту с учётом ключа;
5. создаёт паузы;
6. привязывает альтерации и augmentation dots;
7. группирует головки с одинаковым X в аккорды;
8. пишет MusicXML 4.0.

Рядом создаются:

- `score.analysis.json` — распознанные музыкальные события и предупреждения;
- `score.classification.json` — классификация исходных глифов;
- `score.performance.json` — длительность этапов и счётчики классификатора.

## Быстрый классификатор

Классификация выполняется в несколько этапов:

1. одинаковая нормализованная геометрия классифицируется один раз;
2. каждый глиф превращается в бинарную маску 64×64;
3. все эталоны быстро сравниваются через IoU и `PopCount`;
4. только пять лучших кандидатов сравниваются точным векторным IoU через Clipper2;
5. подготовленные маски и контуры Bravura сохраняются в `References/catalog.bin`.

Первый запуск строит `catalog.bin`. Последующие локальные запуски используют готовый бинарный каталог.

## Golden quality test

```powershell
dotnet test Tests\SvgToMusicXmlPoc.Tests\SvgToMusicXmlPoc.Tests.csproj
```

Тест преобразует `Golden/yellow-leaves-giya-kancheli.svg`, семантически сравнивает результат с исходным MusicXML и создаёт:

```text
TestResults/golden-quality/yellow-leaves-giya-kancheli/
  quality-report.md
  quality-report.csv
  performance.csv
  actual.musicxml
  actual.analysis.json
  actual.classification.json
  actual.performance.json
```

В Markdown-отчёт входят Precision, Recall, F1, пропущенные/лишние события и производительность каждого этапа: парсинг SVG, поиск станов, загрузка каталога, классификация, семантика и запись MusicXML.

## Остальные команды

```powershell
dotnet run -- symbols Samples\score_5.svg
dotnet run -- classify Samples\score_5.svg References\catalog.json classification.json
dotnet run -- analyze Samples\score_5.svg References\catalog.json analysis.json
```

## Что пока не восстановлено полностью

- длительность чёрной головки по штилям, флажкам и вязкам;
- границы тактов;
- голоса и полифония;
- объединение нескольких станов одной системы в фортепианную партию;
- лиги, артикуляция, триоли и текстовые обозначения.

Поэтому текущий MusicXML является диагностическим результатом. Неизвестные или неуверенно распознанные глифы пропускаются и перечисляются в `analysis.json`.
