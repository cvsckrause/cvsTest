# Strategie-Austausch

15. Oktober 2026, 09:32 · 41 Minuten

**Teilnehmer:** Mark Koch, Christian Krause, Torben Drawert, Dominik Meyer, Nicole Hinrichs, Kai Grulke
**Erwähnt:** Adrian (Vorgabe, Meetings mitzuprotokollieren und ins Projekt zu füttern), Jan (Modelle und Kosten), Leon (Netz und Cloud)

Automatische Transkription bereinigt: Grok, Claude Sonnet/Opus, AWS, ZUGFeRD.

## Ergebnis

Es gibt einen Rahmen und einen klickbaren, sehr schmalen ERP-Slice. Fertig ist davon nichts. Die Runde liefert das nächste Produktfeedback, damit die Agents damit weiterarbeiten. Nächstes Treffen: Dienstag.

## Festgelegt danach

Die bestehende Wellenfolge bleibt. Kais Einkaufsbeschreibung geht in Epic E-1: Lieferant, Bestellung aus dem Artikelstamm, Wareneingang von Hand aus dieser Bestellung, optionale Charge mit Einstandspreis je Basiseinheit, Marge der gewählten Charge auf der Auftragsposition. E-Mail-Versand wartet auf ein Belegdokument. ZUGFeRD bleibt E-2.

Teilbare URLs liegen auf R-10. Einen Auftrag wiederfinden liegt auf der Auftragsliste in R-13. Artikellangtext, sichtbare Grid-Zeilen und verschiebbare Spalten warten, bis diese Hülle steht. Torbens übrige Themen bleiben liegen, bis die ersten drei benannt sind.

Nachgetragen in `docs/plans/2026-10-14-roadmap-next-work-items.md`, im Architekturziel unter Einkauf, und in den Issues E-1, R-10 und R-13.

## Stand

Seit Dienstagnachmittag, nach dem Feedback von Adrian, läuft ein erster Slice: Auftrag anlegen, Positionen, Freigabe. Danach passiert wenig. Stammdaten und Lager mit Wareneingang sind angefangen; eine Bestandsreservierung ist angelegt, Lieferung nicht. Der Reiter Rechnen ist leer.

Darunter liegt ein ausgewachsener AWS-Stack (unter anderem Load Balancer, Deployment aus lokalen Containern). Infrastruktur als Code ist weder Terraform noch Pulumi, sondern eine dritte Variante, die der Agent gewählt hat. Sie läuft. Wechseln ist später möglich.

Die Oberfläche ist öffentlich, ohne HTTPS, ohne sauberen Hostnamen, ohne Benutzerkonten und ohne angeschaltete Mehrmandantenfähigkeit. Das ist bewusst zurückgestellt, liegt im Tracker und kommt nach dem Datenmodell. Der fachliche Wert steckt noch nicht in der Anwendung.

Issues und Pull Requests kommen aus der Roadmap. Als Erstes steht eine CI-Prüfung, damit jeder Commit noch baut. Mark arbeitet am Lager, Christian an Stammdaten. „Bauen“ heißt: gemeinsam draufschauen, wenige Fragen beantworten, dann den Agent arbeiten lassen.

## Arbeitsweise

Die erste Anweisung („bau den ersten Slice“) war zu offen. Der Agent ist unter anderem in lokales Linux und Docker abgebogen, als das Budget leer war. Erst die klare Ansage, etwas Klickbares ins Internet zu stellen, hat den Kurs korrigiert. Das ist kein K.-o.-Kriterium.

Cursor Auto bzw. das enthaltene Modell (Grok) reicht für die aktuelle Arbeit und hält die Kosten niedrig. Größere Modelle treffen Vorgaben zuverlässiger, etwa Tags und Assignee aus der Agent-Datei. Eine Roadmap-Frage mit hohem Modell hat das enthaltene Pro-Budget in etwa einer halben bis einer Stunde verbraucht. On-Demand-Nutzung mit Kostengrenze ist seit heute Morgen eingerichtet; die Oberfläche zeigt die Kosten noch nicht. Ein Opus-Lauf ist abgebrochen, weil die Credits leer waren.

Dominik arbeitet parallel mit Codex (Individual-Lizenz, etwa 103 € brutto pro Person und Monat; Business-Preis offen) und sieht denselben Zuverlässigkeitsunterschied. Jan möchte, dass Mark und Dominik Modelle und Abrechnung abgleichen, damit nicht unnötig teuer gearbeitet wird.

Aus dem Hausnetz sind die Werkzeuge nicht nutzbar. Leon hat Bedenken, externen Code in der Cloud sichtbar zu machen. Mark hat den Versuch über den Arbeitsplatz-PC abgebrochen und die Umgebung in etwa einer Stunde auf einem Laptop aufgesetzt.

Zum Tempo: Mark schätzt, das AWS-Setup selbst zu verstehen würde ihn etwa eine Woche kosten; der Agent hat darüber in der Größenordnung von ein bis zwei Stunden nachgedacht. Christian: die Ressourcen von Hand zusammenzuklicken wäre mindestens eine Woche. Dominik: dafür bräuchte es jahrelange Cloud-Erfahrung, die im Team niemand hat. Einzelne Aspekte prüfen sie deshalb nicht mehr im Detail, sondern lassen laufen und ändern später, wenn ein Grund da ist.

## Mitgenommenes Feedback

**Dominik**

- Die URL soll die aktuelle Ansicht abbilden, damit man sie verschicken kann.
- Suche fehlt. Neu angelegte Aufträge sind nicht auffindbar.
- Linkes Menü und der Wechsel nach oben auf dem Handy passen.
- Suche und Dialog auf einer Übersicht stören den Prozessfluss. Lieber trennen.
- Diese Woche UI-/UX-Skills über die Oberfläche laufen lassen.

**Kai** — als Vorgabe für den Agent festgehalten

- Artikellangtext; im Grid einstellbar, wie viele Zeilen sichtbar sind.
- Bestellung an den Lieferanten mit Positionen aus dem Artikelstamm, Versand per E-Mail.
- Bei Wareneingang übernimmt ein Mitarbeiter die Bestellung von Hand in einen Eingangslieferschein. Charge und Einstandspreis je Basiseinheit, wenn der Artikel chargenpflichtig ist oder man es will.
- Im Verkauf die gewählte Charge mit diesem Einstandspreis als sichtbare Marge auf der Auftragsposition.
- Steuersätze auf Verkaufspositionen.
- ZUGFeRD-Rechnung. XRechnung, ZUGFeRD und EN 16931 stehen schon auf der Roadmap. Spalten verschieben ebenfalls.

**Torben** will sehen, was gegenüber dem heutigen ERP möglich ist: Suche, verschiebbare Spalten, Individualisierbarkeit, Datenvisualisierung, Schnittstellen, Belegverfolgung. Noch nicht ausformuliert.

**Christian** als späterer Schritt: unkritische Issues nachts automatisch abarbeiten lassen. Dominik hat bei sich lange GitLab-Runner (etwa 34 Minuten) abgeschaltet, weil die Tests lokal schon laufen, und schickt einen Artikel zu neuem Source Control bei Anthropic in den Chat.

## Offen

- On-Demand-Kosten in der Oberfläche sichtbar machen und die Grenze so setzen, dass die stärkeren Modelle wieder laufen.
- Ab wann das teurere Modell den Aufpreis wert ist.
- Zugang zu dem neueren Modell, das Jan erwähnt hat (im Transkript: „Astra“).
- Torbens Themenliste noch nicht scharf genug für Issues.

## Nächster Schritt

Das Feedback dieser Runde als Nächstes in die Issues geben und damit loslegen. Dienstag wieder zusammenkommen.
