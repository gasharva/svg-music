# SVG → MusicXML PoC v4

Векторный конвертер нотного SVG в MusicXML без промежуточной растеризации.

## Главный сценарий

```powershell
dotnet run -- convert Samples\score_5.svg References\catalog.json score.musicxml
```

`convert` теперь сам выполняет весь конвейер:

1. находит нотные станы;
2. сравнивает каждый SVG `<symbol>` с каталогом Bravura/SMuFL;
3. присваивает экземплярам семантические типы;
4. распознаёт головки нот и их высоту с учётом ключа;
5. создаёт паузы;
6. привязывает альтерации к ближайшей ноте справа;
7. привязывает augmentation dot к ближайшей ноте или паузе слева;
8. группирует головки с одинаковым X в аккорды;
9. пишет MusicXML 4.0.

Рядом создаются:

- `score.analysis.json` — распознанные музыкальные события и предупреждения;
- `score.classification.json` — результат сравнения исходных символов с Bravura.

## Остальные команды

```powershell
dotnet run -- symbols Samples\score_5.svg
dotnet run -- classify Samples\score_5.svg References\catalog.json classification.json
dotnet run -- analyze Samples\score_5.svg References\catalog.json analysis.json
```

`analyze` использует тот же новый классификатор, что и `convert`. Старый ручной `recognition.json` больше не нужен.

## Golden quality test

Эталонная пара находится в `Golden/`:

- `yellow-leaves-giya-kancheli.svg` — вход для движка;
- `yellow-leaves-giya-kancheli.musicxml` — исходный MusicXML.

Запуск:

```powershell
dotnet test Tests\SvgToMusicXmlPoc.Tests\SvgToMusicXmlPoc.Tests.csproj
```

Тест запускает настоящий `ConversionPipeline`, семантически сравнивает полученный MusicXML с эталоном и создаёт:

```text
TestResults/golden-quality/yellow-leaves-giya-kancheli/
├── actual.musicxml
├── actual.analysis.json
├── actual.classification.json
├── quality-report.csv
└── quality-report.md
```

Сравнение выполняется не как буквальный XML diff. Оба документа нормализуются в музыкальные события с учётом `divisions`, `backup`, `forward` и `chord`. Для каждого события отчёт содержит партию, такт, позицию в такте, ожидаемое и найденное значение, расхождения и исходные XML-узлы.

Статусы:

- `Matched` — событие найдено без семантических расхождений;
- `Mismatch` — событие сопоставлено, но отличаются высота, длительность, голос, стан, точки или другие свойства;
- `Missing` — ожидаемое событие не найдено;
- `Extra` — движок создал событие, отсутствующее в эталоне.

GitHub Actions workflow `.github/workflows/golden-quality.yml` запускает тест после push и pull request и публикует отчёты как artifact `golden-musicxml-quality`.

## Что пока не восстановлено полностью

- длительность чёрной головки по штилям, флажкам и вязкам;
- границы тактов;
- голоса и полифония;
- объединение нескольких станов одной системы в фортепианную партию;
- лиги, артикуляция, триоли и текстовые обозначения.

Поэтому текущий MusicXML является рабочим диагностическим результатом, но ещё не гарантирует музыкально точную длительность всех нот. Неизвестные или неуверенно распознанные глифы пропускаются и перечисляются в `analysis.json`, вместо того чтобы ошибочно записываться как ноты.
