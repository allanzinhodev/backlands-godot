"""Gera uma spritesheet de teste no formato que o jogo espera.

Serve para dois fins: provar o carregador (detecção de grade e cor-chave) sem
depender da arte final, e servir de gabarito de dimensões para desenhar em cima.

NÃO é arte do jogo — a saída vai para .playtest/, fora do controle de versão.

Uso:
    python tools/make_test_sheet.py [destino.png]

Formato produzido (o mesmo documentado em docs/spritesheets.md):
    quadro 32x48, grade separada por linhas de #e0e0e0, fundo #9c8b93.
"""

import pathlib
import struct
import sys
import zlib

FRAME_W = 32
FRAME_H = 48
SEPARATOR = 1
COLUMNS = 8
ROWS = 3

GRID = (0xE0, 0xE0, 0xE0)
BACKGROUND = (0x9C, 0x8B, 0x93)

# Rótulo de cada célula (linha, coluna) -> (texto, cor do corpo).
LABELS = {
    (1, 1): ("IF", (230, 80, 80)),
    (1, 2): ("IB", (150, 40, 40)),
    (1, 3): ("WF1", (80, 200, 100)),
    (1, 4): ("WF2", (60, 160, 80)),
    (1, 5): ("WB1", (40, 120, 60)),
    (1, 6): ("WB2", (30, 90, 45)),
    (1, 7): ("LF", (240, 220, 90)),
    (1, 8): ("LB", (190, 170, 60)),
    (2, 1): ("EF", (90, 170, 240)),
    (2, 2): ("EB", (60, 120, 190)),
    (2, 3): ("DF", (120, 120, 130)),
    (2, 4): ("DB", (80, 80, 90)),
    (2, 5): ("CF", (200, 120, 240)),
    (2, 6): ("CB", (150, 80, 190)),
    (3, 1): ("AF1", (250, 150, 60)),
    (3, 2): ("AF2", (210, 110, 30)),
    (3, 3): ("AB1", (170, 90, 30)),
    (3, 4): ("AB2", (130, 70, 20)),
}

# Dígitos/letras 3x5 para escrever o rótulo sem depender de fonte.
GLYPHS = {
    "A": ("010", "101", "111", "101", "101"),
    "B": ("110", "101", "110", "101", "110"),
    "C": ("011", "100", "100", "100", "011"),
    "D": ("110", "101", "101", "101", "110"),
    "E": ("111", "100", "110", "100", "111"),
    "F": ("111", "100", "110", "100", "100"),
    "I": ("111", "010", "010", "010", "111"),
    "L": ("100", "100", "100", "100", "111"),
    "W": ("101", "101", "101", "111", "101"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("110", "001", "010", "100", "111"),
}


def put(pixels, x, y, color):
    if 0 <= x < len(pixels[0]) and 0 <= y < len(pixels):
        pixels[y][x] = color


def draw_text(pixels, x, y, text, color):
    cursor = x
    for char in text:
        glyph = GLYPHS.get(char)
        if glyph is None:
            cursor += 4
            continue
        for row, bits in enumerate(glyph):
            for col, bit in enumerate(bits):
                if bit == "1":
                    put(pixels, cursor + col, y + row, color)
        cursor += 4


def draw_figure(pixels, left, top, label, color):
    """Um bonequinho simples ocupando o quadro, com o rótulo em cima."""
    cx = left + FRAME_W // 2

    # Cabeça
    for y in range(8, 18):
        for x in range(cx - 5, cx + 5):
            put(pixels, x, top + y, color)

    # Corpo
    for y in range(18, 36):
        for x in range(cx - 7, cx + 7):
            put(pixels, x, top + y, color)

    # Pernas, até a base do quadro: é lá que fica o ponto de contato.
    for y in range(36, FRAME_H):
        for x in (cx - 5, cx - 4, cx + 3, cx + 4):
            put(pixels, x, top + y, color)

    # Marca de orientação: um pixel claro no lado direito, para o espelhamento
    # ficar visível de imediato na pré-visualização.
    for y in range(20, 26):
        put(pixels, cx + 6, top + y, (255, 255, 255))

    draw_text(pixels, left + 2, top + 1, label, (0, 0, 0))


def build():
    width = COLUMNS * FRAME_W + (COLUMNS + 1) * SEPARATOR
    height = ROWS * FRAME_H + (ROWS + 1) * SEPARATOR

    pixels = [[GRID for _ in range(width)] for _ in range(height)]

    for row in range(1, ROWS + 1):
        top = SEPARATOR + (row - 1) * (FRAME_H + SEPARATOR)
        for column in range(1, COLUMNS + 1):
            left = SEPARATOR + (column - 1) * (FRAME_W + SEPARATOR)

            for y in range(FRAME_H):
                for x in range(FRAME_W):
                    pixels[top + y][left + x] = BACKGROUND

            entry = LABELS.get((row, column))
            if entry is not None:
                label, color = entry
                draw_figure(pixels, left, top, label, color)

    return width, height, pixels


def write_png(path, width, height, pixels):
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filtro "none"
        for r, g, b in row:
            raw += bytes((r, g, b))

    def chunk(tag, data):
        out = struct.pack(">I", len(data)) + tag + data
        return out + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", header)
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def main():
    destination = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".playtest/teste.png")
    width, height, pixels = build()
    write_png(destination, width, height, pixels)
    print(f"{destination} — {width}x{height}, {COLUMNS}x{ROWS} quadros de {FRAME_W}x{FRAME_H}")


if __name__ == "__main__":
    main()
