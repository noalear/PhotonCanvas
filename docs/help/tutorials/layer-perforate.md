# Perforation and dot mode
<!-- category: Layers and settings -->
<!-- summary: Turn cut and score lines into dashes (fold and tear lines) or rows of single dots. -->
<!-- page: layer-settings.md#perforation-and-dot-mode -->
<!-- order: 130 -->
A perforated line burns in dashes with gaps between: a fold line in card, a tear-off strip. Dot mode
fires a single pulse every so often instead of a line: stippled marks and dotted lines. Both work
on Cut and Score layers.

## A Cut or Score layer
<!-- target: panel:Layers -->
Click the layer with the lines (a Cut or Score layer).

## Dash length
<!-- target: field:Perforate: burn mm -->
Under **More settings**, **Perforate: burn mm**: how long each dash is. 0 is a continuous line.

## Gap
<!-- target: field:then skip mm -->
**then skip mm**: the gap between dashes.

## Or single dots
<!-- target: field:Dot mode every -->
**Dot mode every**: one pulse every this many mm instead of a line (0 = off). Dot mode wins over
perforation.

## How long each dot fires
<!-- target: panel:Layer settings -->
Each dot fires for the layer's **Dot time µs**, 200 µs unless changed. It shows in
**More settings** once **Dot mode every** is above 0.
