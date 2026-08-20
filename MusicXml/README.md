# MusicXml round-trip PoC

Small, dependency-free MusicXML reader/writer experiment.

The first goal is deliberately conservative: read a real `score-partwise` file, expose the compact note fields we care about, write the complete XML tree back without dropping unknown MusicXML elements, then read it again and verify that the typed note projection is unchanged.

```bash
dotnet run --project MusicXml -- Samples/step-by-step/kancheli-mimino-reference.musicxml artifacts/mimino.roundtrip.musicxml
```

The reader currently exposes `part -> measure -> note` plus pitch, alter, octave, duration, voice, type, accidental, stem, staff, chord/rest flags and `default-x/default-y`. The underlying `XDocument` is preserved so unsupported MusicXML structures survive the round-trip.

This is intentionally the safe first step before replacing the internal XML tree with classes generated from the official MusicXML 4.0 XSD. The public `MusicXmlReader` / `MusicXmlWriter` boundary is already separated so the storage implementation can be swapped without touching the future SVG conversion pipeline.
