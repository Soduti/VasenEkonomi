# Projektregler för Vasen

## Gränssnitt och design

- Mobilgränssnittet har högsta prioritet. Desktop anpassas därefter.
- Håll sidorna kompakta. Undvik stora marginaler, onödig padding, tomma ytor och överdimensionerade rubriker.
- Visa återkommande poster i kompakta tabeller när det ger bättre överblick. Använd inte ett separat luftigt kort för varje rad.
- Prioritera att användaren ser relevanta uppgifter och flera listposter samtidigt. Håll filter, summeringar och hjälptexter korta.
- Lägg sekundära inställningar och förklaringar i utfällbara sektioner när de annars tar utrymme från huvuduppgiften.
- Behåll läsbara texter, tydlig tangentbordsfokus och tryckytor som fungerar på mobil. Kompakthet ska främst uppnås genom mindre dekorativt utrymme, inte genom svårläst text eller svårträffade kontroller.
- Anpassa tabellkolumner till små skärmar. Undvik att hela sidan kräver sidledsrullning; kompletterande uppgifter kan visas under huvudtexten i samma tabellcell.

## Teknik och drift

- Ett kvitto kan avslutas med ett lägre uttag än kvittobeloppet. Hela kvittobeloppet räknas fortfarande som utgift. En uttryckligen vald mellanskillnad räknas som eget bidrag, separat från bankinsättningar. Bidraget registreras på avslutsdatumet och räknas inte med efter ångrat avslut.

- C#, ASP.NET Core Razor Pages, JavaScript och SQLite. Inget separat API behövs.
- Driften är IIS på Windows Server via den befintliga Cloudflare-lösningen.
- Bevara Current/Release-upplägget och håll databas, kvitton och inloggningsnycklar utanför publiceringsmapparna.
- Se IMPLEMENTATIONSPLAN.md och IMPLEMENTATIONSSTATUS.md för krav och aktuell implementation.
