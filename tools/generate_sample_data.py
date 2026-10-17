import os
import zipfile

def create_png_image():
    # Minimal 64x64 valid PNG with a blue gradient
    import zlib, struct
    width, height = 64, 64
    raw_data = bytearray()
    for y in range(height):
        raw_data.append(0) # filter type none
        for x in range(width):
            raw_data.extend([int(x * 255 / width), int(y * 255 / height), 220, 255])
    
    def chunk(chunk_type, data):
        return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", zlib.crc32(chunk_type + data) & 0xffffffff)

    png = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png += chunk(b"IHDR", ihdr)
    idat = zlib.compress(raw_data)
    png += chunk(b"IDAT", idat)
    png += chunk(b"IEND", b"")
    return png

def create_sample_docx(target_path):
    os.makedirs(os.path.dirname(target_path), exist_ok=True)
    img_bytes = create_png_image()
    
    content_types = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Default Extension="png" ContentType="image/png"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>"""

    rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>"""

    doc_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rIdImg1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/architecture_diagram.png"/>
</Relationships>"""

    document_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p>
      <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
      <w:r><w:t>Архитектурная спецификация SmartDocs RAG</w:t></w:r>
    </w:p>
    <w:p>
      <w:r><w:t>Система SmartDocs предназначена для индексации разнородных корпоративных документов, включая отчеты в формате Word и Excel, сканы PDF и исходный код. Архитектура построена на двухэтапном поиске: быстрый Bi-Encoder (BGE-M3) и точный Cross-Encoder реранкер.</w:t></w:r>
    </w:p>
    <w:p>
      <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
      <w:r><w:t>Компоненты платформы и целевые показатели SLA</w:t></w:r>
    </w:p>
    <w:tbl>
      <w:tr>
        <w:tc><w:p><w:r><w:t>Компонент</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Назначение</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Движок</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>SLA Uptime</w:t></w:r></w:p></w:tc>
      </w:tr>
      <w:tr>
        <w:tc><w:p><w:r><w:t>Embedding Engine</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Векторизация чанков и запросов</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>BGE-M3 (ONNX Local)</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>99.99%</w:t></w:r></w:p></w:tc>
      </w:tr>
      <w:tr>
        <w:tc><w:p><w:r><w:t>Reranker</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Переранжирование Top-20 кандидатов</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>bge-reranker-v2-m3 (ONNX)</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>99.95%</w:t></w:r></w:p></w:tc>
      </w:tr>
      <w:tr>
        <w:tc><w:p><w:r><w:t>Vector Database</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Хранилище эмбеддингов и фильтрация</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>Qdrant Client</w:t></w:r></w:p></w:tc>
        <w:tc><w:p><w:r><w:t>99.90%</w:t></w:r></w:p></w:tc>
      </w:tr>
    </w:tbl>
    <w:p>
      <w:r><w:t>На приведённой диаграмме показано распределение потоков данных между узлами:</w:t></w:r>
    </w:p>
  </w:body>
</w:document>"""

    with zipfile.ZipFile(target_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", rels)
        zf.writestr("word/_rels/document.xml.rels", doc_rels)
        zf.writestr("word/document.xml", document_xml)
        zf.writestr("word/media/architecture_diagram.png", img_bytes)
    print(f"Created docx: {target_path}")

def create_sample_xlsx(target_path):
    os.makedirs(os.path.dirname(target_path), exist_ok=True)
    
    content_types = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
</Types>"""

    rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>"""

    wb_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
</Relationships>"""

    workbook_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Финансовые результаты Q3" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>"""

    # Rows:
    # 0: Департамент, 1: Выручка (млн руб), 2: Чистая прибыль (млн руб), 3: Рентабельность %
    # 4: Облачные сервисы, 5: 450.5, 6: 112.3, 7: 24.9%
    # 8: Корпоративное ПО, 9: 320.0, 10: 95.4, 11: 29.8%
    # 12: ИИ и Аналитика данных, 13: 180.2, 14: 42.1, 15: 23.3%
    # 16: Кибербезопасность, 17: 210.8, 18: 58.6, 19: 27.8%
    strings = [
        "Департамент (Department)", "Выручка (Revenue, млн руб)", "Чистая прибыль (Net Profit, млн руб)", "Рентабельность %",
        "Облачные сервисы (Cloud Services)", "450.5", "112.3", "24.9%",
        "Корпоративное ПО (Enterprise Software)", "320.0", "95.4", "29.8%",
        "ИИ и Аналитика данных (AI Solutions)", "180.2", "42.1", "23.3%",
        "Кибербезопасность (Cybersecurity)", "210.8", "58.6", "27.8%"
    ]
    
    sst_entries = "".join(f"<si><t>{s}</t></si>" for s in strings)
    shared_strings_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{len(strings)}" uniqueCount="{len(strings)}">
  {sst_entries}
</sst>"""

    sheet1_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetData>
    <row r="1">
      <c r="A1" t="s"><v>0</v></c>
      <c r="B1" t="s"><v>1</v></c>
      <c r="C1" t="s"><v>2</v></c>
      <c r="D1" t="s"><v>3</v></c>
    </row>
    <row r="2">
      <c r="A2" t="s"><v>4</v></c>
      <c r="B2" t="s"><v>5</v></c>
      <c r="C2" t="s"><v>6</v></c>
      <c r="D2" t="s"><v>7</v></c>
    </row>
    <row r="3">
      <c r="A3" t="s"><v>8</v></c>
      <c r="B3" t="s"><v>9</v></c>
      <c r="C3" t="s"><v>10</v></c>
      <c r="D3" t="s"><v>11</v></c>
    </row>
    <row r="4">
      <c r="A4" t="s"><v>12</v></c>
      <c r="B4" t="s"><v>13</v></c>
      <c r="C4" t="s"><v>14</v></c>
      <c r="D4" t="s"><v>15</v></c>
    </row>
    <row r="5">
      <c r="A5" t="s"><v>16</v></c>
      <c r="B5" t="s"><v>17</v></c>
      <c r="C5" t="s"><v>18</v></c>
      <c r="D5" t="s"><v>19</v></c>
    </row>
  </sheetData>
</worksheet>"""

    with zipfile.ZipFile(target_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr("_rels/.rels", rels)
        zf.writestr("xl/_rels/workbook.xml.rels", wb_rels)
        zf.writestr("xl/workbook.xml", workbook_xml)
        zf.writestr("xl/sharedStrings.xml", shared_strings_xml)
        zf.writestr("xl/worksheets/sheet1.xml", sheet1_xml)
    print(f"Created xlsx: {target_path}")

def create_sample_md(target_path):
    os.makedirs(os.path.dirname(target_path), exist_ok=True)
    content = """# Каталог серверного оборудования AI Infrastructure

## 1. Обзор модельного ряда
В данном каталоге приведены спецификации вычислительных узлов для обучения и инференса нейросетевых моделей (LLM, эмбеддинги, компьютерное зрение).

## 2. Технические характеристики GPU-серверов

| Модель сервера | Процессор (CPU) | GPU ускорители | Объем RAM | Интерконнект | Рекомендуемая нагрузка |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Node-AI-Alpha** | 2x AMD EPYC 9654 (192 ядра) | 8x NVIDIA H100 80GB SXM5 | 1.5 TB DDR5 | 8x 400Gb/s InfiniBand | Обучение больших языковых моделей |
| **Node-AI-Beta** | 2x Intel Xeon Platinum 8480+ | 4x NVIDIA L40S 48GB PCIe | 512 GB DDR5 | 2x 100GbE | Инференс LLM, векторизация и RAG |
| **Node-AI-Edge** | 1x AMD EPYC 9124 (16 ядер) | 2x NVIDIA RTX 4090 24GB | 128 GB DDR5 | 2x 25GbE | Локальные эмбеддинги BGE-M3 и OCR |

## 3. Требования к охлаждению и питанию
Для стойки с четырьмя серверами Node-AI-Alpha требуются выделенные линии электропитания мощностью не менее 40 кВт и прецизионное кондиционирование.
"""
    with open(target_path, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"Created md: {target_path}")

def main():
    create_sample_docx("data/technical-docs/architecture_specification.docx")
    create_sample_xlsx("data/financial-reports/q3_financial_report.xlsx")
    create_sample_md("data/product-catalog/server_specifications.md")

if __name__ == "__main__":
    main()
