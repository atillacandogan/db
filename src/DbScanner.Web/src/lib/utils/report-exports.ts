import type { DiscoveryJobDto, DiscoveryResultDto } from "@/schemas/discovery.schema";
import type { FindingListItemDto } from "@/schemas/finding.schema";
import type { ScanDto } from "@/schemas/scan.schema";
import type { Content, TDocumentDefinitions } from "pdfmake/interfaces";
import {
  createSafeFilenamePart,
  downloadCsv,
  downloadJson,
  downloadPdf,
  downloadXlsx,
  type ExportColumn,
  type ExportRow,
} from "@/lib/utils/export-file";

const findingColumns: ExportColumn[] = [
  { key: "severity", header: "Severity", width: 12 },
  { key: "title", header: "Finding", width: 34 },
  { key: "ruleId", header: "Rule ID", width: 32 },
  { key: "targetName", header: "Target", width: 24 },
  { key: "targetHost", header: "Host", width: 22 },
  { key: "targetEngine", header: "Engine", width: 20 },
  { key: "sourceScript", header: "Source Script", width: 28 },
  { key: "scoreContribution", header: "Score Contribution", width: 18 },
  { key: "riskScore", header: "Scan Risk Score", width: 16 },
  { key: "overallSeverity", header: "Overall Severity", width: 16 },
  { key: "scanStatus", header: "Scan Status", width: 14 },
  { key: "status", header: "Finding Status", width: 14 },
  { key: "detectedAt", header: "Detected At", width: 24 },
  { key: "evidence", header: "Evidence", width: 52 },
  { key: "remediation", header: "Remediation", width: 52 },
  { key: "scanId", header: "Scan ID", width: 38 },
  { key: "findingId", header: "Finding ID", width: 38 },
];

export function exportScanAsXlsx(scan: ScanDto) {
  const filename = scanFilename(scan);
  downloadXlsx(filename, [
    {
      name: "Summary",
      columns: [
        { key: "field", header: "Field", width: 24 },
        { key: "value", header: "Value", width: 52 },
      ],
      rows: scanSummaryRows(scan),
    },
    {
      name: "Findings",
      columns: [
        { key: "severity", header: "Severity", width: 12 },
        { key: "title", header: "Finding", width: 34 },
        { key: "ruleId", header: "Rule ID", width: 32 },
        { key: "sourceScript", header: "Source Script", width: 28 },
        { key: "scoreContribution", header: "Score", width: 10 },
        { key: "detectedAt", header: "Detected At", width: 24 },
        { key: "evidence", header: "Evidence", width: 52 },
        { key: "remediation", header: "Remediation", width: 52 },
      ],
      rows: scan.findings.map((finding) => ({
        severity: finding.severity,
        title: finding.title,
        ruleId: finding.ruleId,
        sourceScript: finding.sourceScript,
        scoreContribution: finding.scoreContribution,
        detectedAt: finding.detectedAt,
        evidence: finding.evidence,
        remediation: finding.remediation,
      })),
    },
    {
      name: "Script Results",
      columns: [
        { key: "scriptName", header: "Script Name", width: 32 },
        { key: "nseScriptId", header: "NSE Script ID", width: 30 },
        { key: "status", header: "Status", width: 24 },
        { key: "exitCode", header: "Exit Code", width: 12 },
        { key: "createdAt", header: "Created At", width: 24 },
        { key: "normalizedOutput", header: "Normalized Output", width: 60 },
      ],
      rows: scan.scriptResults.map((result) => ({
        scriptName: result.scriptName,
        nseScriptId: result.nseScriptId,
        status: result.resultStatus,
        exitCode: result.exitCode,
        createdAt: result.createdAt,
        normalizedOutput: result.normalizedOutput,
      })),
    },
  ]);
}

export function exportScanAsJson(scan: ScanDto) {
  downloadJson(scanFilename(scan), {
    exportedAt: new Date().toISOString(),
    report: scan,
  });
}

export async function exportScanAsPdf(scan: ScanDto) {
  const [{ default: pdfMake }, { default: pdfFonts }] = await Promise.all([
    import("pdfmake/build/pdfmake"),
    import("pdfmake/build/vfs_fonts"),
  ]);

  pdfMake.addVirtualFileSystem(pdfFonts);
  const blob = await pdfMake.createPdf(createScanPdfDefinition(scan)).getBlob();
  downloadPdf(scanFilename(scan), blob);
}

export function exportDiscoveryAsXlsx(job: DiscoveryJobDto) {
  downloadXlsx(discoveryFilename(job), [
    {
      name: "Job Summary",
      columns: [
        { key: "field", header: "Field", width: 26 },
        { key: "value", header: "Value", width: 60 },
      ],
      rows: discoverySummaryRows(job),
    },
    {
      name: "Supported Databases",
      columns: discoveryResultColumns,
      rows: job.supportedDatabases.map((result) => discoveryResultRow(result, "Supported")),
    },
    {
      name: "Unsupported Services",
      columns: discoveryResultColumns,
      rows: job.unsupportedDatabases.map((result) => discoveryResultRow(result, "Unsupported")),
    },
  ]);
}

export function exportDiscoveryAsCsv(job: DiscoveryJobDto) {
  downloadCsv(
    discoveryFilename(job),
    discoveryResultColumns,
    [
      ...job.supportedDatabases.map((result) => discoveryResultRow(result, "Supported")),
      ...job.unsupportedDatabases.map((result) => discoveryResultRow(result, "Unsupported")),
    ],
  );
}

export function exportDiscoveryAsJson(job: DiscoveryJobDto) {
  downloadJson(discoveryFilename(job), {
    exportedAt: new Date().toISOString(),
    discoveryJob: job,
  });
}

export function exportFindingsAsXlsx(findings: FindingListItemDto[]) {
  downloadXlsx(findingsFilename(), [
    {
      name: "Latest Findings",
      columns: findingColumns,
      rows: findings.map(findingRow),
    },
  ]);
}

export function exportFindingsAsCsv(findings: FindingListItemDto[]) {
  downloadCsv(findingsFilename(), findingColumns, findings.map(findingRow));
}

function scanSummaryRows(scan: ScanDto): ExportRow[] {
  return [
    { field: "Scan ID", value: scan.id },
    { field: "Target", value: scan.target?.name ?? scan.targetId },
    { field: "Host", value: scan.target?.host },
    { field: "Port", value: scan.target?.port },
    { field: "Engine", value: scan.target?.engine },
    { field: "Status", value: scan.status },
    { field: "Risk Score", value: scan.riskScore },
    { field: "Overall Severity", value: scan.overallSeverity },
    { field: "Created At", value: scan.createdAt },
    { field: "Started At", value: scan.startedAt },
    { field: "Finished At", value: scan.finishedAt },
    { field: "Finding Count", value: scan.findings.length },
    { field: "Executed Script Count", value: scan.scriptResults.length },
    { field: "Error", value: scan.errorMessage },
  ];
}

export function createScanPdfDefinition(scan: ScanDto): TDocumentDefinitions {
  const targetName = scan.target?.name ?? scan.targetId;
  const content: Content[] = [
    { text: "DATABASE SCANNER POC", style: "eyebrow" },
    { text: "Scan Report", style: "title" },
    {
      text: `${pdfText(targetName)} - ${pdfText(scan.id)}`,
      style: "subtitle",
    },
    {
      text: `Rapor oluşturulma zamanı: ${formatPdfDate(new Date().toISOString())}`,
      style: "documentMeta",
      margin: [0, 0, 0, 16],
    },
    sectionHeading("Scan Summary"),
    {
      table: {
        widths: [110, "*"],
        body: scanSummaryRows(scan).map((row) => [
          { text: pdfText(row.field), style: "summaryLabel" },
          { text: pdfText(row.value), style: "summaryValue" },
        ]),
      },
      layout: "lightHorizontalLines",
      margin: [0, 0, 0, 18],
    },
    sectionHeading(`Findings (${scan.findings.length})`),
  ];

  if (scan.findings.length === 0) {
    content.push({ text: "Bu scan için finding bulunmuyor.", style: "emptyState", margin: [0, 0, 0, 18] });
  } else {
    scan.findings.forEach((finding, index) => {
      const severity = finding.severity.toUpperCase();
      content.push({
        stack: [
          {
            columns: [
              { text: `${index + 1}. ${pdfText(finding.title)}`, style: "cardTitle", width: "*" },
              {
                text: severity,
                style: "severityBadge",
                color: severityColor(finding.severity),
                width: "auto",
              },
            ],
            columnGap: 10,
          },
          {
            text: `${pdfText(finding.ruleId)}  |  Script: ${pdfText(finding.sourceScript)}  |  Score: +${finding.scoreContribution}`,
            style: "cardMeta",
          },
          { text: "Evidence", style: "fieldLabel" },
          { text: pdfText(finding.evidence), style: "body" },
          { text: "Remediation", style: "fieldLabel", margin: [0, 6, 0, 2] },
          { text: pdfText(finding.remediation), style: "body" },
        ],
        style: "card",
        margin: [0, 0, 0, 10],
      });
    });
  }

  content.push(sectionHeading(`Script Results (${scan.scriptResults.length})`));

  if (scan.scriptResults.length === 0) {
    content.push({ text: "Bu scan için script sonucu bulunmuyor.", style: "emptyState" });
  } else {
    scan.scriptResults.forEach((result, index) => {
      content.push({
        stack: [
          { text: `${index + 1}. ${pdfText(result.scriptName)}`, style: "cardTitle" },
          {
            text: `NSE Script: ${pdfText(result.nseScriptId)}  |  Status: ${pdfText(result.resultStatus)}  |  Exit Code: ${pdfText(result.exitCode)}`,
            style: "cardMeta",
          },
          { text: "Normalized Output", style: "fieldLabel" },
          { text: pdfText(result.normalizedOutput), style: "code" },
        ],
        style: "card",
        margin: [0, 0, 0, 10],
      });
    });
  }

  return {
    info: {
      title: `Scan Report - ${targetName}`,
      author: "Database Scanner POC",
      subject: `Scan ${scan.id}`,
    },
    pageSize: "A4",
    pageMargins: [42, 48, 42, 46],
    content,
    footer: (currentPage, pageCount) => ({
      text: `Database Scanner POC  |  Sayfa ${currentPage} / ${pageCount}`,
      alignment: "center",
      color: "#667085",
      fontSize: 8,
      margin: [0, 14, 0, 0],
    }),
    defaultStyle: {
      font: "Roboto",
      fontSize: 9,
      color: "#182230",
      lineHeight: 1.25,
    },
    styles: {
      eyebrow: { fontSize: 8, bold: true, color: "#3448c5", characterSpacing: 0.7 },
      title: { fontSize: 24, bold: true, color: "#101828", margin: [0, 4, 0, 3] },
      subtitle: { fontSize: 10, color: "#475467", margin: [0, 0, 0, 3] },
      documentMeta: { fontSize: 8, color: "#667085" },
      sectionHeading: { fontSize: 14, bold: true, color: "#101828", margin: [0, 6, 0, 8] },
      summaryLabel: { bold: true, color: "#344054", margin: [3, 4, 4, 4] },
      summaryValue: { color: "#182230", margin: [3, 4, 4, 4] },
      card: { fillColor: "#F8FAFC" },
      cardTitle: { fontSize: 10, bold: true, color: "#101828", margin: [8, 7, 8, 2] },
      cardMeta: { fontSize: 8, color: "#667085", margin: [8, 0, 8, 6] },
      severityBadge: { fontSize: 8, bold: true, alignment: "right", margin: [4, 8, 8, 0] },
      fieldLabel: { fontSize: 8, bold: true, color: "#344054", margin: [8, 2, 8, 2] },
      body: { fontSize: 9, color: "#344054", margin: [8, 0, 8, 7] },
      code: { fontSize: 8, color: "#344054", margin: [8, 0, 8, 8] },
      emptyState: { italics: true, color: "#667085" },
    },
  };
}

function sectionHeading(text: string): Content {
  return { text, style: "sectionHeading" };
}

function pdfText(value: unknown) {
  if (value === null || value === undefined || value === "") {
    return "-";
  }

  return String(value)
    .replace(/\r\n/g, "\n")
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/g, " ")
    .trim() || "-";
}

function formatPdfDate(value?: string | null) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function severityColor(severity: string) {
  switch (severity.toLowerCase()) {
    case "critical":
      return "#B42318";
    case "high":
      return "#C4320A";
    case "medium":
      return "#B54708";
    case "low":
      return "#175CD3";
    default:
      return "#475467";
  }
}

const discoveryResultColumns: ExportColumn[] = [
  { key: "group", header: "Result Group", width: 18 },
  { key: "host", header: "Host", width: 24 },
  { key: "port", header: "Port", width: 10 },
  { key: "detectedService", header: "Detected Service", width: 28 },
  { key: "detectedEngine", header: "Detected Engine", width: 18 },
  { key: "confidence", header: "Confidence (%)", width: 15 },
  { key: "fingerprintLevel", header: "Fingerprint Level", width: 18 },
  { key: "environment", header: "Environment", width: 14 },
  { key: "status", header: "Status", width: 20 },
  { key: "targetState", header: "Target State", width: 20 },
  { key: "canAddAsTarget", header: "Can Add As Target", width: 18 },
  { key: "isAlreadyTarget", header: "Already Target", width: 16 },
  { key: "reason", header: "Reason", width: 52 },
  { key: "resultId", header: "Result ID", width: 38 },
  { key: "targetId", header: "Target ID", width: 38 },
];

function discoverySummaryRows(job: DiscoveryJobDto): ExportRow[] {
  return [
    { field: "Job ID", value: job.id },
    { field: "Name", value: job.name },
    { field: "Status", value: job.status },
    { field: "Scope", value: job.scope },
    { field: "Environment", value: job.environment },
    { field: "Engine Filter", value: job.supportedEngineFilter.join(", ") },
    { field: "Created At", value: job.createdAt },
    { field: "Started At", value: job.startedAt },
    { field: "Finished At", value: job.finishedAt },
    { field: "Supported Result Count", value: job.supportedDatabases.length },
    { field: "Unsupported Result Count", value: job.unsupportedDatabases.length },
    { field: "Error", value: job.errorMessage },
  ];
}

function discoveryResultRow(result: DiscoveryResultDto, group: string): ExportRow {
  return {
    group,
    host: result.host,
    port: result.port,
    detectedService: result.detectedService,
    detectedEngine: result.detectedEngine,
    confidence: result.confidence,
    fingerprintLevel: result.fingerprintLevel,
    environment: result.environment,
    status: result.status,
    targetState: result.targetState,
    canAddAsTarget: result.canAddAsTarget,
    isAlreadyTarget: result.isAlreadyTarget,
    reason: result.reason,
    resultId: result.id,
    targetId: result.existingTargetId ?? result.targetId,
  };
}

function findingRow(finding: FindingListItemDto): ExportRow {
  return {
    severity: finding.severity,
    title: finding.title,
    ruleId: finding.ruleId,
    targetName: finding.targetName,
    targetHost: finding.targetHost,
    targetEngine: finding.targetEngine,
    sourceScript: finding.sourceScript,
    scoreContribution: finding.scoreContribution,
    riskScore: finding.riskScore,
    overallSeverity: finding.overallSeverity,
    scanStatus: finding.scanStatus,
    status: finding.status ?? "open",
    detectedAt: finding.detectedAt,
    evidence: finding.evidence,
    remediation: finding.remediation,
    scanId: finding.scanId,
    findingId: finding.id,
  };
}

function scanFilename(scan: ScanDto) {
  const target = createSafeFilenamePart(scan.target?.name ?? "target");
  return `scan-report-${target}-${scan.id.slice(0, 8)}`;
}

function discoveryFilename(job: DiscoveryJobDto) {
  return `discovery-results-${createSafeFilenamePart(job.name)}-${job.id.slice(0, 8)}`;
}

function findingsFilename() {
  return `findings-${new Date().toISOString().slice(0, 10)}`;
}
