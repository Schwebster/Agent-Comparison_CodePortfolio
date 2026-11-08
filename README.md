# Agent-Comparison – Codeportfolio

**C#-Codeportfolio einer Unity-Simulationsumgebung für eine Bachelorarbeit zum Vergleich von NPC-Entscheidungsarchitekturen.**

In einer Siedlungssimulation sammeln autonome Charaktere Nahrung und Holz, errichten Gebäude und erkunden eine prozedural erzeugte Welt. Das Projekt untersucht, wie unterschiedliche Entscheidungsverfahren diese Aufgaben unter vergleichbaren Bedingungen steuern.

## Mein Beitrag

Ich entwickle die Simulationslogik und die Entscheidungsarchitekturen in C# mit Unity. Die eigenen 3D-Assets bestehen aus einfachen geometrischen Formen. Schwerpunkte sind die Trennung von Entscheidung und Aktionsausführung, nachvollziehbare Agentenentscheidungen und die Erfassung von Simulationsereignissen für spätere Auswertungen.

## Implementierte Funktionen

- **Finite State Machine (FSM)** zur zustandsbasierten Steuerung der NPCs.
- **Goal-Oriented Action Planning (GOAP)** mit symbolischen Weltzuständen, Vorbedingungen, Effekten und kostenbasierter Plansuche.
- **Gemeinsame Aktionsausführung** für Ressourcenbeschaffung, Ablieferung, Erkundung und Bau.
- **Rasterbasierte Weltgenerierung** mit Ressourcenverteilung und schrittweiser Aufdeckung der Karte.
- **Siedlungssimulation** mit Ressourcenverwaltung, Wohnraum und Tagesabläufen.
- **Ereignisprotokollierung im JSON-Lines-Format** für Entscheidungen, Aufgaben und Weltveränderungen.
- **EditMode- und PlayMode-Tests** für Teile der GOAP-Planung und -Ausführung.

## Projektstand

Das Repository enthält den Entwicklungsstand der Bachelorarbeit mit implementierten FSM- und GOAP-Varianten. Eine LLM-gestützte Erweiterung ist geplant. Der Umfang dieser Erweiterung und die abschließende vergleichende Auswertung sind noch offen.

## Einstieg in den Code

- [Agentenschnittstellen und Entscheidungsmodell](Assets/Scripts/Agents/Core)
- [FSM-Steuerung](Assets/Scripts/Agents/FSM)
- [GOAP-Planer und Domänenmodell](Assets/Scripts/Agents/GOAP/Core)
- [Gemeinsame Ausführungslogik](Assets/Scripts/Agents/Common)
- [Simulationsprotokollierung](Assets/Scripts/Simulation/Logging)
- [GOAP-Tests](Assets/Tests)

## Umfang dieses Portfolios

Dieser Auszug enthält 105 C#-Dateien und die zugehörigen Assembly-Definitionen aus den Ordnern Assets/Scripts, Assets/Tests und Assets/Editor. Die ursprüngliche Ordnerstruktur ist erhalten. Enthalten sind außerdem die Editor-Werkzeuge zur GOAP-Validierung.

Das zugrunde liegende Projekt verwendet Unity 6000.2.10f1. Diese Kopie dient der Codeeinsicht. Zum Ausführen werden zusätzlich die Unity-Projektkonfiguration, Paketabhängigkeiten, Szenen, Prefabs und weiteren Assets des vollständigen Projekts benötigt. Auch die enthaltenen Tests setzen die passende Unity-Projektumgebung voraus.

Die Kopie enthält ausschließlich Quellcode, Assembly-Definitionen und diese Beschreibung. Sie hat keine übernommene Git-Versionsgeschichte.
