export type ExportValue = string | number | boolean | null | undefined;

export type ExportColumn = {
  key: string;
  header: string;
  width?: number;
};

export type ExportRow = Record<string, ExportValue>;

export type ExportSheet = {
  name: string;
  columns: ExportColumn[];
  rows: ExportRow[];
};

const textEncoder = new TextEncoder();

export function downloadCsv(filename: string, columns: ExportColumn[], rows: ExportRow[]) {
  const header = columns.map((column) => escapeCsvValue(column.header));
  const body = rows.map((row) => columns.map((column) => escapeCsvValue(row[column.key])).join(","));
  const content = `\uFEFF${[header.join(","), ...body].join("\r\n")}`;

  downloadBlob(normalizeFilename(filename, "csv"), new Blob([content], { type: "text/csv;charset=utf-8" }));
}

export function downloadJson(filename: string, value: unknown) {
  const content = `${JSON.stringify(value, null, 2)}\n`;
  downloadBlob(normalizeFilename(filename, "json"), new Blob([content], { type: "application/json;charset=utf-8" }));
}

export function downloadPdf(filename: string, value: Blob) {
  downloadBlob(
    normalizeFilename(filename, "pdf"),
    value.type === "application/pdf" ? value : new Blob([value], { type: "application/pdf" }),
  );
}

export function downloadXlsx(filename: string, sheets: ExportSheet[]) {
  if (sheets.length === 0) {
    throw new Error("Excel export requires at least one sheet.");
  }

  const normalizedSheets = normalizeSheets(sheets);
  const entries: ZipEntry[] = [
    { name: "[Content_Types].xml", data: encodeXml(createContentTypes(normalizedSheets.length)) },
    { name: "_rels/.rels", data: encodeXml(createRootRelationships()) },
    { name: "xl/workbook.xml", data: encodeXml(createWorkbook(normalizedSheets)) },
    { name: "xl/_rels/workbook.xml.rels", data: encodeXml(createWorkbookRelationships(normalizedSheets.length)) },
    { name: "xl/styles.xml", data: encodeXml(createStyles()) },
    ...normalizedSheets.map((sheet, index) => ({
      name: `xl/worksheets/sheet${index + 1}.xml`,
      data: encodeXml(createWorksheet(sheet)),
    })),
  ];
  const archive = createZip(entries);

  downloadBlob(
    normalizeFilename(filename, "xlsx"),
    new Blob([archive.slice().buffer as ArrayBuffer], {
      type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    }),
  );
}

export function createSafeFilenamePart(value: string, fallback = "export") {
  const cleaned = value
    .normalize("NFKC")
    .replace(/[<>:"/\\|?*\u0000-\u001F]/g, "-")
    .replace(/\s+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^[.\s-]+|[.\s-]+$/g, "")
    .slice(0, 96);

  return cleaned || fallback;
}

function escapeCsvValue(value: ExportValue) {
  if (value === null || value === undefined) {
    return "";
  }

  let text = String(value);
  if (/^[=+\-@\t\r]/.test(text)) {
    text = `'${text}`;
  }

  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function normalizeFilename(filename: string, extension: string) {
  const withoutExtension = filename.replace(new RegExp(`\\.${extension}$`, "i"), "");
  return `${createSafeFilenamePart(withoutExtension)}.${extension}`;
}

function downloadBlob(filename: string, blob: Blob) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.style.display = "none";
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

type NormalizedSheet = ExportSheet & { name: string };

function normalizeSheets(sheets: ExportSheet[]): NormalizedSheet[] {
  const usedNames = new Set<string>();

  return sheets.map((sheet, index) => {
    const baseName = sheet.name.replace(/[\\/?*\[\]:]/g, " ").trim().slice(0, 31) || `Sheet ${index + 1}`;
    let name = baseName;
    let suffix = 2;

    while (usedNames.has(name.toLocaleLowerCase("en-US"))) {
      const suffixText = ` ${suffix}`;
      name = `${baseName.slice(0, 31 - suffixText.length)}${suffixText}`;
      suffix += 1;
    }

    usedNames.add(name.toLocaleLowerCase("en-US"));
    return { ...sheet, name };
  });
}

function createContentTypes(sheetCount: number) {
  const sheets = Array.from({ length: sheetCount }, (_, index) =>
    `<Override PartName="/xl/worksheets/sheet${index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`,
  ).join("");

  return xmlDocument(
    `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">` +
      `<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>` +
      `<Default Extension="xml" ContentType="application/xml"/>` +
      `<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>` +
      `<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>` +
      sheets +
      `</Types>`,
  );
}

function createRootRelationships() {
  return xmlDocument(
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">` +
      `<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>` +
      `</Relationships>`,
  );
}

function createWorkbook(sheets: NormalizedSheet[]) {
  const sheetNodes = sheets
    .map((sheet, index) => `<sheet name="${escapeXml(sheet.name)}" sheetId="${index + 1}" r:id="rId${index + 1}"/>`)
    .join("");

  return xmlDocument(
    `<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ` +
      `xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">` +
      `<sheets>${sheetNodes}</sheets></workbook>`,
  );
}

function createWorkbookRelationships(sheetCount: number) {
  const sheets = Array.from({ length: sheetCount }, (_, index) =>
    `<Relationship Id="rId${index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet${index + 1}.xml"/>`,
  ).join("");

  return xmlDocument(
    `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">` +
      sheets +
      `<Relationship Id="rId${sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>` +
      `</Relationships>`,
  );
}

function createStyles() {
  return xmlDocument(
    `<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
      `<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts>` +
      `<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF3F51B5"/><bgColor indexed="64"/></patternFill></fill></fills>` +
      `<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD0D5DD"/></left><right style="thin"><color rgb="FFD0D5DD"/></right><top style="thin"><color rgb="FFD0D5DD"/></top><bottom style="thin"><color rgb="FFD0D5DD"/></bottom><diagonal/></border></borders>` +
      `<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>` +
      `<cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"><alignment vertical="top" wrapText="1"/></xf></cellXfs>` +
      `<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>` +
      `</styleSheet>`,
  );
}

function createWorksheet(sheet: NormalizedSheet) {
  const columnCount = Math.max(1, sheet.columns.length);
  const lastColumn = columnName(columnCount);
  const lastRow = Math.max(1, sheet.rows.length + 1);
  const columns = sheet.columns
    .map((column, index) => `<col min="${index + 1}" max="${index + 1}" width="${clamp(column.width ?? 18, 8, 60)}" customWidth="1"/>`)
    .join("");
  const headerCells = sheet.columns
    .map((column, index) => createCell(`${columnName(index + 1)}1`, column.header, 1))
    .join("");
  const rows = sheet.rows
    .map((row, rowIndex) => {
      const excelRow = rowIndex + 2;
      const cells = sheet.columns
        .map((column, columnIndex) => createCell(`${columnName(columnIndex + 1)}${excelRow}`, row[column.key], 2))
        .join("");
      return `<row r="${excelRow}">${cells}</row>`;
    })
    .join("");
  const autoFilter = sheet.columns.length > 0 ? `<autoFilter ref="A1:${lastColumn}${lastRow}"/>` : "";

  return xmlDocument(
    `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
      `<dimension ref="A1:${lastColumn}${lastRow}"/>` +
      `<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>` +
      `<cols>${columns}</cols><sheetData><row r="1">${headerCells}</row>${rows}</sheetData>${autoFilter}` +
      `</worksheet>`,
  );
}

function createCell(reference: string, value: ExportValue, style: number) {
  if (value === null || value === undefined) {
    return `<c r="${reference}" s="${style}"/>`;
  }

  if (typeof value === "number" && Number.isFinite(value)) {
    return `<c r="${reference}" s="${style}"><v>${value}</v></c>`;
  }

  if (typeof value === "boolean") {
    return `<c r="${reference}" s="${style}" t="b"><v>${value ? 1 : 0}</v></c>`;
  }

  return `<c r="${reference}" s="${style}" t="inlineStr"><is><t xml:space="preserve">${escapeXml(String(value))}</t></is></c>`;
}

function columnName(index: number) {
  let value = index;
  let name = "";

  while (value > 0) {
    value -= 1;
    name = String.fromCharCode(65 + (value % 26)) + name;
    value = Math.floor(value / 26);
  }

  return name;
}

function escapeXml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

function xmlDocument(content: string) {
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>${content}`;
}

function encodeXml(value: string) {
  return textEncoder.encode(value);
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, value));
}

type ZipEntry = {
  name: string;
  data: Uint8Array;
};

function createZip(entries: ZipEntry[]) {
  const localParts: Uint8Array[] = [];
  const centralParts: Uint8Array[] = [];
  let localOffset = 0;
  const { time, date } = dosDateTime(new Date());

  for (const entry of entries) {
    const name = textEncoder.encode(entry.name);
    const checksum = crc32(entry.data);
    const localHeader = createLocalHeader(name.length, checksum, entry.data.length, time, date);
    localParts.push(localHeader, name, entry.data);

    const centralHeader = createCentralHeader(name.length, checksum, entry.data.length, localOffset, time, date);
    centralParts.push(centralHeader, name);
    localOffset += localHeader.length + name.length + entry.data.length;
  }

  const centralDirectory = concatenate(centralParts);
  const end = createEndOfCentralDirectory(entries.length, centralDirectory.length, localOffset);
  return concatenate([...localParts, centralDirectory, end]);
}

function createLocalHeader(nameLength: number, checksum: number, size: number, time: number, date: number) {
  const header = new Uint8Array(30);
  const view = new DataView(header.buffer);
  view.setUint32(0, 0x04034b50, true);
  view.setUint16(4, 20, true);
  view.setUint16(6, 0x0800, true);
  view.setUint16(8, 0, true);
  view.setUint16(10, time, true);
  view.setUint16(12, date, true);
  view.setUint32(14, checksum, true);
  view.setUint32(18, size, true);
  view.setUint32(22, size, true);
  view.setUint16(26, nameLength, true);
  view.setUint16(28, 0, true);
  return header;
}

function createCentralHeader(nameLength: number, checksum: number, size: number, offset: number, time: number, date: number) {
  const header = new Uint8Array(46);
  const view = new DataView(header.buffer);
  view.setUint32(0, 0x02014b50, true);
  view.setUint16(4, 20, true);
  view.setUint16(6, 20, true);
  view.setUint16(8, 0x0800, true);
  view.setUint16(10, 0, true);
  view.setUint16(12, time, true);
  view.setUint16(14, date, true);
  view.setUint32(16, checksum, true);
  view.setUint32(20, size, true);
  view.setUint32(24, size, true);
  view.setUint16(28, nameLength, true);
  view.setUint16(30, 0, true);
  view.setUint16(32, 0, true);
  view.setUint16(34, 0, true);
  view.setUint16(36, 0, true);
  view.setUint32(38, 0, true);
  view.setUint32(42, offset, true);
  return header;
}

function createEndOfCentralDirectory(entryCount: number, centralSize: number, centralOffset: number) {
  const end = new Uint8Array(22);
  const view = new DataView(end.buffer);
  view.setUint32(0, 0x06054b50, true);
  view.setUint16(4, 0, true);
  view.setUint16(6, 0, true);
  view.setUint16(8, entryCount, true);
  view.setUint16(10, entryCount, true);
  view.setUint32(12, centralSize, true);
  view.setUint32(16, centralOffset, true);
  view.setUint16(20, 0, true);
  return end;
}

function concatenate(parts: Uint8Array[]) {
  const totalLength = parts.reduce((total, part) => total + part.length, 0);
  const result = new Uint8Array(totalLength);
  let offset = 0;

  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }

  return result;
}

function dosDateTime(value: Date) {
  const year = Math.max(1980, value.getFullYear());
  return {
    time: (value.getHours() << 11) | (value.getMinutes() << 5) | Math.floor(value.getSeconds() / 2),
    date: ((year - 1980) << 9) | ((value.getMonth() + 1) << 5) | value.getDate(),
  };
}

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit += 1) {
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  }
  return value >>> 0;
});

function crc32(data: Uint8Array) {
  let checksum = 0xffffffff;
  for (const byte of data) {
    checksum = crcTable[(checksum ^ byte) & 0xff] ^ (checksum >>> 8);
  }
  return (checksum ^ 0xffffffff) >>> 0;
}
