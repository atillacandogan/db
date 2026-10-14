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
      name: "Scan Check Results",
      columns: [
        { key: "scriptName", header: "Scan Check", width: 32 },
        { key: "scanCheckId", header: "Scan Check ID", width: 30 },
        { key: "status", header: "Status", width: 24 },
        { key: "exitCode", header: "Exit Code", width: 12 },
        { key: "createdAt", header: "Created At", width: 24 },
        { key: "normalizedOutput", header: "Normalized Output", width: 60 },
      ],
      rows: scan.scriptResults.map((result) => ({
        scriptName: result.scriptName,
        scanCheckId: result.scanCheckId,
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
  const blob = await createPdfBlob(createScanPdfDefinition(scan));
  downloadPdf(scanFilename(scan), blob);
}

export async function exportDiscoveryAsPdf(job: DiscoveryJobDto) {
  const blob = await createPdfBlob(createDiscoveryPdfDefinition(job));
  downloadPdf(discoveryFilename(job), blob);
}

export async function exportFindingsAsPdf(findings: FindingListItemDto[]) {
  const blob = await createPdfBlob(createFindingsPdfDefinition(findings));
  downloadPdf(findingsFilename(), blob);
}

async function createPdfBlob(definition: TDocumentDefinitions) {
  const [{ default: pdfMake }, { default: pdfFonts }] = await Promise.all([
    import("pdfmake/build/pdfmake"),
    import("pdfmake/build/vfs_fonts"),
  ]);

  pdfMake.addVirtualFileSystem(pdfFonts);
  return pdfMake.createPdf(definition).getBlob();
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

export function createDiscoveryPdfDefinition(job: DiscoveryJobDto): TDocumentDefinitions {
  const allResults = [...job.supportedDatabases, ...job.unsupportedDatabases];
  const confirmedCount = allResults.filter((result) => result.fingerprintLevel === "Confirmed").length;
  const averageConfidence = allResults.length > 0
    ? Math.round(allResults.reduce((total, result) => total + result.confidence, 0) / allResults.length)
    : 0;
  const content: Content[] = [
    reportCover(
      "Database Discovery Report",
      `${pdfText(job.name)} - ${pdfText(job.id)}`,
      "Credentialless service discovery and protocol fingerprint assessment",
    ),
    metricCards([
      { label: "Supported databases", value: job.supportedDatabases.length, color: "#067647" },
      { label: "Unsupported services", value: job.unsupportedDatabases.length, color: "#B54708" },
      { label: "Confirmed fingerprints", value: confirmedCount, color: "#3448C5" },
      { label: "Average confidence", value: `${averageConfidence}%`, color: "#175CD3" },
    ]),
    {
      text: discoveryExecutiveSummary(job, confirmedCount),
      style: "lead",
      margin: [0, 12, 0, 16],
    },
    sectionHeading("Discovery scope"),
    summaryTable([
      ["Status", job.status],
      ["Scope", job.scope.replaceAll("\n", ", ")],
      ["Environment", job.environment],
      ["Engine filter", job.supportedEngineFilter.join(", ")],
      ["Created", formatPdfDate(job.createdAt)],
      ["Started", formatPdfDate(job.startedAt)],
      ["Finished", formatPdfDate(job.finishedAt)],
      ...(job.errorMessage ? [["Job error", job.errorMessage] as [string, unknown]] : []),
    ]),
    sectionHeading(`Supported databases (${job.supportedDatabases.length})`),
  ];

  if (job.supportedDatabases.length === 0) {
    content.push(emptyReportState("No supported database service was confirmed in this discovery scope."));
  } else {
    job.supportedDatabases.forEach((result, index) => {
      content.push(discoveryResultBlock(result, index, true));
    });
  }

  content.push(sectionHeading(`Unsupported / informational services (${job.unsupportedDatabases.length})`));
  if (job.unsupportedDatabases.length === 0) {
    content.push(emptyReportState("No unsupported or informational service was recorded."));
  } else {
    job.unsupportedDatabases.forEach((result, index) => {
      content.push(discoveryResultBlock(result, index, false));
    });
  }

  content.push(
    sectionHeading("Interpretation notes"),
    {
      ul: [
        "Confirmed means a database protocol response passed the fingerprint validation rules.",
        "Confidence is an assessment signal, not proof that authentication or authorization will succeed.",
        "PortHint and unconfirmed services are informational and cannot be added as targets.",
        "The scanned port identifies the reachable service endpoint; the protocol response identifies the database engine.",
      ],
      style: "noteList",
      margin: [0, 0, 0, 8],
    },
  );

  return executiveDocument(
    `Database Discovery Report - ${job.name}`,
    `Discovery job ${job.id}`,
    content,
    "Discovery Report",
  );
}

export function createFindingsPdfDefinition(findings: FindingListItemDto[]): TDocumentDefinitions {
  const sortedFindings = [...findings].sort((left, right) => {
    const severityDifference = severityRank(right.severity) - severityRank(left.severity);
    return severityDifference || new Date(right.detectedAt).getTime() - new Date(left.detectedAt).getTime();
  });
  const counts = {
    critical: countSeverity(findings, "critical"),
    high: countSeverity(findings, "high"),
    medium: countSeverity(findings, "medium"),
    low: countSeverity(findings, "low"),
    info: countSeverity(findings, "info"),
  };
  const openCount = findings.filter((finding) => (finding.status ?? "open") === "open").length;
  const uniqueTargets = new Set(findings.map((finding) => finding.targetId)).size;
  const content: Content[] = [
    reportCover(
      "Security Findings Report",
      `${findings.length} findings across ${uniqueTargets} targets`,
      "Risk overview, supporting evidence, and prioritized remediation guidance",
    ),
    metricCards([
      { label: "Critical", value: counts.critical, color: "#B42318" },
      { label: "High", value: counts.high, color: "#C4320A" },
      { label: "Medium", value: counts.medium, color: "#B54708" },
      { label: "Low", value: counts.low, color: "#175CD3" },
      { label: "Info", value: counts.info, color: "#475467" },
    ]),
    {
      text: findingsExecutiveSummary(findings.length, openCount, uniqueTargets, counts),
      style: "lead",
      margin: [0, 12, 0, 16],
    },
    sectionHeading("Risk overview"),
    summaryTable([
      ["Total findings", findings.length],
      ["Open findings", openCount],
      ["Resolved findings", findings.length - openCount],
      ["Affected targets", uniqueTargets],
      ["Critical / High", `${counts.critical} / ${counts.high}`],
      ["Medium / Low / Info", `${counts.medium} / ${counts.low} / ${counts.info}`],
      ["Report generated", formatPdfDate(new Date().toISOString())],
    ]),
    sectionHeading(`Prioritized findings (${findings.length})`),
  ];

  if (sortedFindings.length === 0) {
    content.push(emptyReportState("No security findings were available at report generation time."));
  } else {
    sortedFindings.forEach((finding, index) => {
      const severity = finding.severity.toUpperCase();
      const status = finding.status ?? "open";
      const detailStack: Content[] = [
        {
          columns: [
            { text: `${index + 1}. ${pdfText(finding.title)}`, style: "resultTitle", width: "*" },
            { text: severity, style: "resultBadge", color: severityColor(finding.severity), width: "auto" },
          ],
          columnGap: 12,
        },
        {
          text: `${pdfText(finding.targetName)} (${pdfText(finding.targetHost)})  |  ${pdfText(finding.targetEngine)}  |  ${pdfText(status)}`,
          style: "resultMeta",
        },
        {
          text: `Rule: ${pdfText(finding.ruleId)}  |  Source: ${pdfText(finding.sourceScript)}  |  Score contribution: +${pdfText(finding.scoreContribution)}  |  Detected: ${formatPdfDate(finding.detectedAt)}`,
          style: "resultMeta",
          margin: [0, 0, 0, 7],
        },
      ];

      if (finding.description) {
        detailStack.push({ text: "Finding detail", style: "fieldLabel" });
        detailStack.push({ text: pdfText(finding.description), style: "body" });
      }

      detailStack.push({ text: "Evidence", style: "fieldLabel", margin: [0, 6, 0, 2] });
      detailStack.push({ text: pdfText(finding.evidence), style: "evidence" });
      detailStack.push({ text: "Remediation", style: "fieldLabel", margin: [0, 7, 0, 2] });
      detailStack.push({ text: pdfText(finding.remediation), style: "body" });

      content.push(reportDetailBlock(detailStack));
    });
  }

  content.push(
    sectionHeading("Prioritization guidance"),
    {
      ol: [
        "Address Critical and High findings first, especially those affecting externally reachable or production targets.",
        "Validate the evidence against the current target state before making a production change.",
        "Apply the remediation, rerun the relevant scan, and close the finding only after verification.",
      ],
      style: "noteList",
    },
  );

  return executiveDocument(
    `Security Findings Report - ${new Date().toISOString().slice(0, 10)}`,
    `${findings.length} security findings`,
    content,
    "Findings Report",
  );
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

  content.push(sectionHeading(`Scan Check Results (${scan.scriptResults.length})`));

  if (scan.scriptResults.length === 0) {
    content.push({ text: "Bu scan için scan check sonucu bulunmuyor.", style: "emptyState" });
  } else {
    scan.scriptResults.forEach((result, index) => {
      content.push({
        stack: [
          { text: `${index + 1}. ${pdfText(result.scriptName)}`, style: "cardTitle" },
          {
            text: `Scan Check: ${pdfText(result.scanCheckId)}  |  Status: ${pdfText(result.resultStatus)}  |  Exit Code: ${pdfText(result.exitCode)}`,
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

type ReportMetric = {
  label: string;
  value: string | number;
  color: string;
};

function reportCover(title: string, subtitle: string, description: string): Content {
  return {
    stack: [
      { text: "DATABASE SCANNER POC", style: "eyebrow" },
      { text: title, style: "title" },
      { text: pdfText(subtitle), style: "subtitle" },
      { text: description, style: "coverDescription" },
      {
        text: `Generated: ${formatPdfDate(new Date().toISOString())}`,
        style: "documentMeta",
      },
    ],
    margin: [0, 0, 0, 18],
  };
}

function metricCards(metrics: ReportMetric[]): Content {
  return {
    columns: metrics.map((metric) => ({
      width: "*",
      table: {
        widths: ["*"],
        body: [[{
          stack: [
            { text: pdfText(metric.value), fontSize: 18, bold: true, color: metric.color },
            { text: metric.label, fontSize: 7.5, color: "#475467", margin: [0, 3, 0, 0] },
          ],
          fillColor: "#F8FAFC",
          margin: [8, 8, 8, 8],
        }]],
      },
      layout: "noBorders",
    })),
    columnGap: 8,
    margin: [0, 0, 0, 2],
  };
}

function summaryTable(rows: Array<[string, unknown]>): Content {
  return {
    table: {
      widths: [118, "*"],
      body: rows.map(([label, value]) => [
        { text: label, style: "summaryLabel", fillColor: "#F8FAFC" },
        { text: pdfText(value), style: "summaryValue" },
      ]),
    },
    layout: {
      hLineColor: () => "#E4E7EC",
      vLineColor: () => "#E4E7EC",
      hLineWidth: () => 0.5,
      vLineWidth: () => 0.5,
      paddingLeft: () => 7,
      paddingRight: () => 7,
      paddingTop: () => 5,
      paddingBottom: () => 5,
    },
    margin: [0, 0, 0, 18],
  };
}

function discoveryResultBlock(result: DiscoveryResultDto, index: number, supported: boolean): Content {
  const engineOrService = result.detectedEngine ?? result.detectedService;
  const badgeColor = supported ? "#067647" : "#B54708";
  const detailStack: Content[] = [
    {
      columns: [
        {
          text: `${index + 1}. ${pdfText(engineOrService)} - ${pdfText(result.host)}:${result.port}`,
          style: "resultTitle",
          width: "*",
        },
        {
          text: supported ? "SUPPORTED" : "INFORMATIONAL",
          style: "resultBadge",
          color: badgeColor,
          width: "auto",
        },
      ],
      columnGap: 12,
    },
    {
      text: `Confidence: ${result.confidence}%  |  Fingerprint: ${pdfText(result.fingerprintLevel)}  |  Environment: ${pdfText(result.environment)}`,
      style: "resultMeta",
    },
    {
      text: `Status: ${pdfText(result.status)}  |  Target state: ${pdfText(result.targetState)}  |  Add as target: ${result.canAddAsTarget ? "Yes" : "No"}`,
      style: "resultMeta",
      margin: [0, 0, 0, 7],
    },
    { text: supported ? "Verification / reason" : "Why this service is not targetable", style: "fieldLabel" },
    { text: pdfText(result.reason), style: "body" },
  ];

  return reportDetailBlock(detailStack);
}

function reportDetailBlock(stack: Content[]): Content {
  return {
    table: {
      widths: [4, "*"],
      dontBreakRows: true,
      body: [[
        { text: "", fillColor: "#6172F3" },
        { stack, fillColor: "#F8FAFC", margin: [10, 8, 10, 9] },
      ]],
    },
    layout: "noBorders",
    margin: [0, 0, 0, 10],
  };
}

function emptyReportState(text: string): Content {
  return {
    text,
    style: "emptyState",
    fillColor: "#F8FAFC",
    margin: [10, 8, 10, 16],
  };
}

function executiveDocument(
  title: string,
  subject: string,
  content: Content[],
  footerLabel: string,
): TDocumentDefinitions {
  return {
    info: {
      title,
      author: "Database Scanner POC",
      subject,
    },
    pageSize: "A4",
    pageMargins: [42, 46, 42, 44],
    content,
    footer: (currentPage, pageCount) => ({
      columns: [
        { text: `Database Scanner POC - ${footerLabel}`, alignment: "left" },
        { text: `Page ${currentPage} / ${pageCount}`, alignment: "right" },
      ],
      color: "#667085",
      fontSize: 8,
      margin: [42, 13, 42, 0],
    }),
    defaultStyle: {
      font: "Roboto",
      fontSize: 9,
      color: "#182230",
      lineHeight: 1.25,
    },
    styles: {
      eyebrow: { fontSize: 8, bold: true, color: "#3448C5", characterSpacing: 0.8 },
      title: { fontSize: 25, bold: true, color: "#101828", margin: [0, 5, 0, 3] },
      subtitle: { fontSize: 10, color: "#475467", margin: [0, 0, 0, 7] },
      coverDescription: { fontSize: 9, color: "#344054", margin: [0, 0, 0, 3] },
      documentMeta: { fontSize: 8, color: "#667085" },
      lead: { fontSize: 10, color: "#344054", lineHeight: 1.35 },
      sectionHeading: { fontSize: 14, bold: true, color: "#101828", margin: [0, 7, 0, 8] },
      summaryLabel: { bold: true, color: "#344054" },
      summaryValue: { color: "#182230" },
      resultTitle: { fontSize: 10.5, bold: true, color: "#101828" },
      resultBadge: { fontSize: 7.5, bold: true, alignment: "right", margin: [6, 1, 0, 0] },
      resultMeta: { fontSize: 8, color: "#667085", margin: [0, 4, 0, 0] },
      fieldLabel: { fontSize: 8, bold: true, color: "#344054", margin: [0, 1, 0, 2] },
      body: { fontSize: 9, color: "#344054" },
      evidence: { fontSize: 8.5, color: "#344054", fillColor: "#FFFFFF", margin: [7, 6, 7, 6] },
      emptyState: { italics: true, color: "#667085" },
      noteList: { fontSize: 8.5, color: "#475467", lineHeight: 1.3 },
    },
  };
}

function discoveryExecutiveSummary(job: DiscoveryJobDto, confirmedCount: number) {
  const total = job.supportedDatabases.length + job.unsupportedDatabases.length;
  if (total === 0) {
    return "The discovery job completed without recording a reachable database candidate or informational service.";
  }

  return `${total} reachable service result(s) were recorded in the selected scope. ${job.supportedDatabases.length} result(s) are supported and targetable; ${job.unsupportedDatabases.length} are informational or unsupported. ${confirmedCount} result(s) passed a confirmed database protocol fingerprint.`;
}

function findingsExecutiveSummary(
  total: number,
  openCount: number,
  uniqueTargets: number,
  counts: { critical: number; high: number; medium: number; low: number; info: number },
) {
  if (total === 0) {
    return "No findings were available in the exported snapshot. This does not replace periodic scanning or target coverage review.";
  }

  const priority = counts.critical > 0
    ? `Immediate attention is recommended for ${counts.critical} Critical finding(s).`
    : counts.high > 0
      ? `${counts.high} High finding(s) should be prioritized.`
      : "No Critical or High finding is present in this snapshot.";

  return `${total} finding(s) affect ${uniqueTargets} target(s), with ${openCount} currently open. ${priority}`;
}

function countSeverity(findings: FindingListItemDto[], severity: string) {
  return findings.filter((finding) => finding.severity.toLowerCase() === severity).length;
}

function severityRank(severity: string) {
  switch (severity.toLowerCase()) {
    case "critical":
      return 4;
    case "high":
      return 3;
    case "medium":
      return 2;
    case "low":
      return 1;
    default:
      return 0;
  }
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
