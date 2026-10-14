import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { runInThisContext } from "node:vm";
import test from "node:test";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import ts from "typescript";

const requirePackage = createRequire(import.meta.url);
const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../src");
const adminRole = "dbscanner-admin";
const user = {
  id: "keycloak-user-123", username: "cagri", firstName: "Çağrı", lastName: "Yılmaz",
  enabled: true, role: adminRole, mfaRequired: false, mfaConfigured: false,
  lastLoginAt: "2026-10-11T12:00:00Z",
};
const page = (items) => ({ items, totalCount: items.length, totalPages: 1, page: 1, pageSize: 20 });
const success = (data) => ({ data, isPending: false, isLoading: false, isSuccess: true,
  isError: false, isFetching: false, refetch: async () => {} });
const pending = () => ({ isPending: true, isLoading: true, isSuccess: false,
  isError: false, isFetching: true, refetch: async () => {} });
const failure = () => ({ isPending: false, isLoading: false, isSuccess: false,
  isError: true, isFetching: false, error: new Error("Servise ulaşılamıyor."), refetch: async () => {} });

// Render the actual TSX components with deterministic query/auth fixtures. No
// network requests, browser session, or live data writes are made by these tests.
function ui({ queries = {}, roles = [adminRole], pathname = "/dashboard", api = {}, states,
  mutations = [], queryConfigurations = [], queryClient = {}, router = {}, mutationPending = false,
  elements = [], submittedActions } = {}) {
  const defaults = {
    dashboard: success({ targets: [], rules: [], scripts: [] }),
    "admin-users": success(page([user])),
    "access-me": success({ tenantId: "local", dedicatedTenant: false }),
    "resource-access-users": success(page([user])),
    "resource-access-audit": success(page([])),
    "resource-access": success({ projects: [{ id: "p1", name: "Production" }],
      teams: [{ id: "t1", name: "Security", isArchived: false }],
      members: [], grants: [], targets: [] }),
  };
  const unexpectedRequest = () => { throw new Error("A rendering test attempted a network request."); };
  const jsxRuntime = requirePackage("react/jsx-runtime");
  let stateIndex = 0;
  const mocks = {
    "react/jsx-runtime": { ...jsxRuntime, ...Object.fromEntries(["jsx", "jsxs"].map(method => [method, (type, props, key) => {
      const element = jsxRuntime[method](type, props, key);
      elements.push(element);
      return element;
    }])) },
    "next/link": { __esModule: true, default: ({ children, href, ...props }) =>
      React.createElement("a", { href, ...props }, children) },
    "next/navigation": { usePathname: () => pathname, useRouter: () => ({ replace: () => {}, ...router }) },
    "@/components/providers/auth-provider": { useAuth: () => ({ userId: "current-admin",
      username: "test-admin", roles, hasRole: (role) => roles.includes(role), logout: async () => {} }) },
    "@/lib/auth/keycloak": { initializeKeycloak: unexpectedRequest },
    "@tanstack/react-query": {
      useQuery: (options) => {
        queryConfigurations.push(options);
        return options.enabled === false ? pending()
          : queries[options.queryKey[0]] ?? defaults[options.queryKey[0]] ?? pending();
      },
      useMutation: (options) => { mutations.push(options); return { isPending: mutationPending,
        mutate: submittedActions ? variables => submittedActions.push({ options, variables }) : unexpectedRequest }; },
      useQueryClient: () => ({ invalidateQueries: async () => {}, removeQueries: () => {}, ...queryClient }),
    },
    "@/lib/api/client": Object.fromEntries(["apiGet", "apiPost", "apiPut", "apiPatch", "apiDelete"]
      .map((method) => [method, api[method] ?? unexpectedRequest])),
  };
  // Optional hook snapshots let confirmation and saving states be rendered
  // without submitting forms, accessing a browser, or changing live records.
  if (states) mocks.react = { ...React, useState: (initial) => {
    const index = stateIndex++;
    return [index in states ? states[index] : typeof initial === "function" ? initial() : initial, () => {}];
  } };
  const cache = new Map();
  function load(path) {
    const base = resolve(sourceRoot, path);
    const filename = [base, `${base}.tsx`, `${base}.ts`].find(existsSync);
    assert.ok(filename, `Missing UI module: ${path}`);
    if (cache.has(filename)) return cache.get(filename).exports;
    const loadedModule = { exports: {} };
    cache.set(filename, loadedModule);
    const output = ts.transpileModule(readFileSync(filename, "utf8"), {
      compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS,
        jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true }, fileName: filename,
    }).outputText;
    const localRequire = (specifier) => {
      if (specifier in mocks) return mocks[specifier];
      if (specifier.startsWith("@/")) return load(specifier.slice(2));
      if (specifier.startsWith(".")) return load(resolve(dirname(filename), specifier));
      return requirePackage(specifier);
    };
    runInThisContext(`(function(exports, require, module) { ${output}\n})`, { filename })
      (loadedModule.exports, localRequire, loadedModule);
    return loadedModule.exports;
  }
  return {
    load,
    render: (path, name = "default", props = {}) =>
      renderToStaticMarkup(React.createElement(load(path)[name], props)),
  };
}

function labelledSection(html, headingId) {
  const section = html.match(new RegExp(`<section[^>]*aria-labelledby="${headingId}"[^>]*>([\\s\\S]*?)<\\/section>`));
  assert.ok(section, `Missing section labelled by ${headingId}`);
  assert.match(section[1], new RegExp(`<h2 id="${headingId}">`));
  return section[1];
}

const scopedDirectory = {
  projects: [{ id: "project-production-42", name: "Üretim Projesi" },
    { id: "project-integration-17", name: "Entegrasyon Projesi" }],
  teams: [{ id: "team-security-31", name: "Güvenlik Ekibi", isArchived: false },
    { id: "team-platform-58", name: "Platform Ekibi", isArchived: false }],
  members: [{ teamId: "team-security-31", subject: user.id },
    { teamId: "team-platform-58", subject: "platform-member-1" },
    { teamId: "team-platform-58", subject: "platform-member-2" }],
  grants: [],
  targets: [{ id: "target-production-1", name: "Üretim PostgreSQL", projectId: "project-production-42" },
    { id: "target-production-2", name: "Üretim MySQL", projectId: "project-production-42" },
    { id: "target-integration-1", name: "Entegrasyon SQL", projectId: "project-integration-17" }],
};

function renderScopedDirectory(data = scopedDirectory) {
  return ui({ queries: { "resource-access": success(data) } })
    .render("components/admin/resource-access-client", "ResourceAccessClient");
}

test("navigation uses English labels without changing the admin-only routes", () => {
  const { navigationItems } = ui().load("lib/const/navigation");
  assert.deepEqual(navigationItems.filter(item => item.requiredRole).map(item => [item.href, item.label, item.requiredRole]), [
    ["/admin/users", "Access Management", adminRole],
    ["/admin/resource-access", "Teams and Target Permissions", adminRole],
  ]);
});

test("admin shell keeps only the sidebar in English and uses Turkish admin breadcrumbs/sign-out", () => {
  const html = ui({ pathname: "/admin/users" }).render("components/layout/app-shell", "AppShell");
  assert.match(html, /<aside[^>]*class="app-sidebar"[^>]*lang="en"/);
  assert.match(html, /<header[^>]*class="app-header"[^>]*lang="tr"/);
  assert.match(html, /Overview \/ Erişim Yönetimi/);
  assert.match(html, /Access Management/);
  assert.match(html, /Teams and Target Permissions/);
  assert.match(html, /Çıkış/);
  assert.doesNotMatch(html, /Sign Out/);
  assert.match(html, /href="\/admin\/users"/);
  const accessHtml = ui({ pathname: "/admin/resource-access" }).render("components/layout/app-shell", "AppShell");
  assert.match(accessHtml, /Overview \/ Ekip ve Hedef Yetkileri/);
});

test("viewer shell still hides both administration routes", () => {
  const html = ui({ roles: ["dbscanner-viewer"] }).render("components/layout/app-shell", "AppShell");
  assert.doesNotMatch(html, /href="\/admin\//);
  assert.match(html, /Viewer/);
});

test("empty dashboard and coverage render Turkish recovery guidance", () => {
  const html = ui().render("app/dashboard/page");
  for (const text of ["Henüz tarama yok.", "Tüm Taramaları Gör", "Katalogda kural kaydı yok.",
    "Katalogda tarama kontrolü yok.", "Risk özeti için tamamlanmış bir tarama bekleniyor."]) assert.ok(html.includes(text), text);
  assert.match(html, /lang="tr"/);
  assert.doesNotMatch(html, /lang="en"/);
});

test("dashboard displays actual target names, scan links and Turkish dates", () => {
  const target = { id: "target1", name: "Üretim Veritabanı", host: "database.internal", port: 5432,
    engine: "postgresql", enabled: true, lastScan: { scanId: "scan1", createdAt: "2026-10-11T12:00:00Z",
      status: "scanned", riskScore: 25, overallSeverity: "low" } };
  const html = ui({ queries: { dashboard: success({ targets: [target], rules: [], scripts: [] }) } }).render("app/dashboard/page");
  assert.match(html, /Üretim Veritabanı/);
  assert.match(html, /href="\/scans\/scan1"/);
  assert.match(html, /Tarama Detayını Gör/);
  assert.match(html, /Düşük/);
  assert.ok(html.includes(new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short" }).format(new Date(target.lastScan.createdAt))));
  assert.doesNotMatch(html, /Oct/);
});

for (const [state, result, text] of [["loading", pending(), "Dashboard yükleniyor..."],
  ["error", failure(), "Dashboard yüklenemedi."]]) {
  test(`dashboard ${state} copy is Turkish`, () => {
    const html = ui({ queries: { dashboard: result } }).render("app/dashboard/page");
    assert.ok(html.includes(text));
    assert.match(html, /lang="tr"/);
    if (state === "error") assert.match(html, /Tekrar Dene/);
  });
}

test("route loading/error boundaries have Turkish accessible copy", () => {
  const loading = ui().render("app/dashboard/loading");
  const error = ui().render("app/dashboard/error", "default", { reset: () => {} });
  assert.match(loading, /aria-label="Dashboard yükleniyor"/);
  assert.match(loading, /lang="tr"/);
  assert.match(error, /API bağlantınızı kontrol edip tekrar deneyin./);
  assert.match(error, /lang="tr"/);
});

test("non-admin route guard stays closed with Turkish access-denied copy", () => {
  const html = ui({ roles: ["dbscanner-viewer"] }).render("components/auth/admin-route-guard", "AdminRouteGuard", { children: "PRIVATE CONTENT" });
  assert.match(html, /Erişim Engellendi/);
  assert.match(html, /lang="tr"/);
  assert.doesNotMatch(html, /PRIVATE CONTENT/);
});

test("sign-in preparation uses Turkish without starting a live session", () => {
  const html = ui().render("components/providers/auth-provider.tsx", "AuthProvider");
  assert.match(html, /Güvenli oturum hazırlanıyor/);
  assert.match(html, /lang="tr"/);
});

test("user directory is Turkish while real names, dates and Keycloak role values remain intact", () => {
  const html = ui().render("components/admin/admin-users-client", "AdminUsersClient");
  for (const text of ["Kullanıcılar ve Yetkiler", "Kullanıcı Oluştur", "Kullanıcı ara", "Yönetici", "Çağrı Yılmaz"]) assert.ok(html.includes(text), text);
  assert.match(html, /lang="tr"/);
  assert.ok(html.includes(new Date(user.lastLoginAt).toLocaleString("tr-TR")));
  assert.match(html, /value="dbscanner-admin"/);
  assert.doesNotMatch(html, /Invite User|Send Invitation|Kullanıcı Davet Et|Davet Gönder/);
});

for (const [state, result, text] of [["loading", pending(), "Kimlik servisi kontrol ediliyor"],
  ["error", failure(), "Bağlantı sorunu"]]) {
  test(`user directory ${state} feedback is Turkish`, () => {
    assert.ok(ui({ queries: { "admin-users": result } }).render("components/admin/admin-users-client", "AdminUsersClient").includes(text));
  });
}

test("dedicated tenant does not expose shared-realm user creation", () => {
  const html = ui({ queries: { "access-me": success({ dedicatedTenant: true }) } })
    .render("components/admin/tenant-user-management", "TenantUserManagement");
  assert.match(html, /Kimlik Yönetimi/);
  assert.match(html, /Ekip ve Hedef Yetkileri/);
  assert.match(html, /lang="tr"/);
  assert.doesNotMatch(html, /Kullanıcı Oluştur/);
});

test("user picker shows real Keycloak IDs and Turkish selected-user copy", () => {
  const html = ui().render("components/admin/resource-access-user-picker", "ResourceAccessUserPicker", { selected: user, onSelect: () => {} });
  for (const text of ["Keycloak kullanıcılarında ara", "ID’ler doğrudan Keycloak’tan gelir", "keycloak-user-123", "Çağrı Yılmaz", "Seçildi"]) assert.ok(html.includes(text), text);
  assert.match(html, /lang="tr"/);
  assert.match(html, /aria-pressed="true"/);
});

test("multi-user picker preserves selected users and marks existing members", () => {
  const existingMember = { ...user, id: "already-in-team", username: "existing" };
  const html = ui({ queries: { "resource-access-users": success(page([user, existingMember])) } })
    .render("components/admin/resource-access-user-picker", "ResourceAccessUserPicker", {
      selectedUsers: [user], onToggleUser() {}, existingUserIds: [existingMember.id],
    });
  assert.match(html, /Birden fazla kullanıcı seçebilirsiniz/);
  assert.match(html, /1 kullanıcı seçildi/);
  assert.match(html, /Seçilen: @cagri|@cagri/);
  assert.match(html, /aria-pressed="true"/);
  assert.match(html, /Ekip üyesi/);
  assert.match(html, /disabled="" aria-pressed="false"/);
});

test("dedicated user picker remains disabled until tenant-scoped identity is configured", () => {
  const html = ui({ queries: { "access-me": success({ dedicatedTenant: true }) } })
    .render("components/admin/resource-access-user-picker", "ResourceAccessUserPicker", { selected: null, onSelect: () => {} });
  assert.match(html, /Kullanıcı araması için kuruma özel bir kimlik dizini gereklidir/);
  assert.match(html, /disabled=""/);
});

test("directory keeps the two-card project/team grid and places recent audit below it", () => {
  const queryConfigurations = [];
  const html = ui({ queryConfigurations }).render("components/admin/resource-access-client", "ResourceAccessClient");
  for (const text of ["Ekip ve Hedef Yetkileri", "Yeni proje adı", "Yeni ekip adı", "Hedefleri proje detaylarından, üyeleri ve izinleri ekip detaylarından yönetin"]) assert.ok(html.includes(text), text);
  assert.match(html, /class="resource-access-grid"/);
  assert.match(html, /lang="tr"/);
  assert.equal((html.match(/<section\b/g) ?? []).length, 3);
  assert.ok(html.indexOf("resource-access-audit\"") > html.indexOf("</section>", html.indexOf("resource-access-teams")));
  assert.doesNotMatch(html, /Keycloak kullanıcılarında ara|Etkin erişim önizlemesi/);
  assert.match(html, /<table>/);
  assert.deepEqual(queryConfigurations.map(query => query.queryKey[0]), ["resource-access", "access-me", "resource-access-audit"]);
});

test("projects and teams have separate labelled sections, creation forms and directory lists", () => {
  const html = renderScopedDirectory();
  const projects = labelledSection(html, "projects-heading");
  const teams = labelledSection(html, "teams-heading");
  assert.match(projects, /<h2 id="projects-heading">Projeler<\/h2>/);
  assert.match(teams, /<h2 id="teams-heading">Ekipler<\/h2>/);
  assert.equal((projects.match(/<form\b/g) ?? []).length, 1);
  assert.equal((teams.match(/<form\b/g) ?? []).length, 1);
  assert.match(projects, /for="project-name"/);
  assert.match(projects, /id="project-name"/);
  assert.match(projects, /Proje oluştur/);
  assert.match(teams, /for="team-name"/);
  assert.match(teams, /id="team-name"/);
  assert.match(teams, /Ekip oluştur/);
  assert.match(projects, /<ul class="resource-access-directory-list"[^>]*>/);
  assert.match(teams, /<ul class="resource-access-directory-list"[^>]*>/);
  assert.doesNotMatch(projects, /id="team-name"|Güvenlik Ekibi|Platform Ekibi/);
  assert.doesNotMatch(teams, /id="project-name"|Üretim Projesi|Entegrasyon Projesi/);
});

test("separate project/team cards preserve real names and detail identifiers without exposing unrelated controls", () => {
  const html = renderScopedDirectory();
  for (const project of scopedDirectory.projects) {
    assert.ok(labelledSection(html, "projects-heading").includes(project.name));
    assert.ok(html.includes(`href="/admin/resource-access/projects/${project.id}"`));
  }
  for (const team of scopedDirectory.teams) {
    assert.ok(labelledSection(html, "teams-heading").includes(team.name));
    assert.ok(html.includes(`href="/admin/resource-access/teams/${team.id}"`));
  }
  for (const target of scopedDirectory.targets) {
    assert.ok(!html.includes(target.name));
    assert.ok(!html.includes(`id="target-project-${target.id}"`));
  }
  assert.doesNotMatch(html, /keycloak-user-123|platform-member-1|platform-member-2|members-heading/);
});

test("project target counts and team member counts stay inside their own cards", () => {
  const html = renderScopedDirectory();
  const projects = labelledSection(html, "projects-heading");
  const teams = labelledSection(html, "teams-heading");
  assert.match(projects, /<strong>Üretim Projesi<\/strong><small>2 hedef<\/small>/);
  assert.match(projects, /<strong>Entegrasyon Projesi<\/strong><small>1 hedef<\/small>/);
  assert.match(teams, /<strong>Güvenlik Ekibi<\/strong><small>1 üye · Aktif<\/small>/);
  assert.match(teams, /<strong>Platform Ekibi<\/strong><small>2 üye · Aktif<\/small>/);
  assert.doesNotMatch(projects, /\d+ üye/);
  assert.doesNotMatch(teams, /\d+ hedef/);
});

test("empty project and team sections give distinct Turkish setup guidance", () => {
  const html = renderScopedDirectory({ projects: [], teams: [], members: [], grants: [], targets: [] });
  const projects = labelledSection(html, "projects-heading");
  const teams = labelledSection(html, "teams-heading");
  assert.match(projects, /Henüz proje oluşturulmadı/);
  assert.match(teams, /Henüz ekip oluşturulmadı/);
  assert.doesNotMatch(projects, /Henüz ekip oluşturulmadı/);
  assert.doesNotMatch(teams, /Henüz proje oluşturulmadı/);
  assert.equal((html.match(/<form\b/g) ?? []).length, 2);
});

test("directory creation preserves trimmed names and refreshes only its directory/audit queries", async () => {
  const elements = [], submittedActions = [], calls = [], refreshed = [];
  ui({ elements, submittedActions, states: ["  Yeni Proje  ", "  Yeni Ekip  "],
    api: { apiPost: async (path, body) => { calls.push({ path, body }); } },
    queryClient: { invalidateQueries: async options => refreshed.push(options.queryKey) },
  }).render("components/admin/resource-access-client", "ResourceAccessClient");
  for (const form of elements.filter(element => element.type === "form")) form.props.onSubmit({ preventDefault() {} });
  for (const action of submittedActions) await action.options.mutationFn(action.variables);
  assert.deepEqual(calls, [{ path: "/api/access/projects", body: { name: "Yeni Proje" } },
    { path: "/api/access/teams", body: { name: "Yeni Ekip" } }]);
  await submittedActions[0].options.onSuccess();
  assert.deepEqual(refreshed, [["resource-access"], ["resource-access-audit"]]);
});

const emptyProject = {
  project: { id: "project-42", name: "Üretim Projesi", isDefault: false },
  targets: [], grants: [], teams: [],
};
const populatedProject = {
  ...emptyProject,
  targets: [{ id: "target-17", name: "Üretim PostgreSQL", projectId: "project-42" }],
  teams: [{ id: "team-31", name: "Güvenlik Ekibi", isArchived: false },
    { id: "team-58", name: "Eski Ekip", isArchived: true }],
  grants: [{ id: "grant-1", teamId: "team-31", projectId: "project-42", targetId: null, permissions: 3 },
    { id: "grant-2", teamId: "team-31", projectId: "project-42", targetId: "target-17", permissions: 5 },
    { id: "grant-3", teamId: "team-58", projectId: "project-42", targetId: null, permissions: 1 }],
};

function renderProject(data = emptyProject, options = {}) {
  return ui({ ...options, queries: { "resource-access-project": success(data), ...options.queries } })
    .render("components/admin/resource-access-project-detail", "ResourceAccessProjectDetail", { projectId: data.project.id });
}

test("project rows have discoverable detail links like team rows", () => {
  const projects = labelledSection(renderScopedDirectory(), "projects-heading");
  for (const project of scopedDirectory.projects) {
    assert.ok(projects.includes(`href="/admin/resource-access/projects/${project.id}"`));
  }
  assert.equal((projects.match(/Detaylar →/g) ?? []).length, scopedDirectory.projects.length);
});

test("project detail route remains admin-only", async () => {
  const fixture = ui({ roles: ["dbscanner-viewer"] });
  const { default: ProjectPage } = fixture.load("app/admin/resource-access/projects/[id]/page");
  const element = await ProjectPage({ params: Promise.resolve({ id: "project-42" }) });
  const html = renderToStaticMarkup(element);
  assert.match(html, /Erişim Engellendi/);
  assert.doesNotMatch(html, /Proje bilgileri|Projeyi sil/);
});

test("project detail shows scoped targets, distinct team count and archived grants", () => {
  const html = renderProject(populatedProject);
  for (const text of ["Üretim Projesi", "Proje bilgileri", "Üretim PostgreSQL", "Güvenlik Ekibi", "Eski Ekip",
    "Arşivlenmiş · İzin etkin değil", "Projedeki tüm hedefler", "Görüntüle", "Tarama / durdurma", "Hedefi yönet", "Denetim geçmişi"]) assert.ok(html.includes(text), text);
  assert.match(html, /<dt>Hedef<\/dt><dd>1<\/dd>/);
  assert.match(html, /<dt>Ekip<\/dt><dd>2<\/dd>/);
  assert.match(html, /<dt>İzin<\/dt><dd>3<\/dd>/);
  assert.match(html, /href="\/admin\/resource-access\/teams\/team-31#team-permissions"/);
  assert.match(html, /Mevcut hedefi bu projeye bağla/);
  assert.match(html, /Başka projeye taşı/);
  assert.doesNotMatch(html, /resource-access#target-project-management|resource-access#registered-permissions|resource-access#project-permissions/);
  assert.match(html, /href="\/targets"/);
  assert.match(html, /lang="tr"/);
});

test("empty project detail has separate setup messages and no automatic destructive confirmation", () => {
  const html = renderProject();
  assert.match(html, /Bu projeye henüz hedef bağlanmadı/);
  assert.match(html, /Bu projeye henüz ekip izni verilmedi/);
  assert.match(html, /<button[^>]*type="submit"[^>]*disabled=""[^>]*>Adı kaydet<\/button>/);
  assert.match(html, /<button(?=[^>]*type="button")(?=[^>]*class="button button--danger")[^>]*>Projeyi sil<\/button>/);
  assert.doesNotMatch(html, /Kalıcı silmeyi onayla|id="project-delete-confirmation"/);
});

test("default project can be renamed but cannot be deleted even when empty", () => {
  const html = renderProject({ ...emptyProject, project: { ...emptyProject.project, isDefault: true } }, { states: ["Yeni varsayılan"] });
  assert.match(html, /VARSAYILAN PROJE/);
  assert.match(html, /Varsayılan proje silinemez/);
  assert.match(html, /Adını değiştirebilirsiniz/);
  assert.match(html, /<button[^>]*class="button button--danger"[^>]*disabled=""[^>]*aria-describedby="project-delete-blockers"[^>]*>Projeyi sil<\/button>/);
  assert.match(html, /value="Yeni varsayılan"/);
  assert.doesNotMatch(html, /type="submit" disabled=""[^>]*>Adı kaydet/);
});

test("bound targets and grants explain why delete is disabled", () => {
  const html = renderProject(populatedProject);
  assert.match(html, /1 bağlı hedef var/);
  assert.match(html, /3 kayıtlı ekip izni var/);
  assert.match(html, /arşivlenmiş ekiplerin izinleri de buna dahildir/);
  assert.match(html, /class="button button--danger"[^>]*disabled=""/);
});

test("project deletion confirmation requires an exact name, is cancellable and remains disabled while saving", () => {
  const wrong = renderProject(emptyProject, { states: [null, true, "yanlış"] });
  assert.match(wrong, /Bu işlem geri alınamaz/);
  assert.match(wrong, /for="project-delete-confirmation"/);
  assert.match(wrong, /aria-describedby="project-delete-help"/);
  assert.match(wrong, /class="button button--danger"[^>]*disabled=""[^>]*>Kalıcı silmeyi onayla/);
  assert.match(wrong, /İptal/);
  const matched = renderProject(emptyProject, { states: [null, true, emptyProject.project.name] });
  assert.match(matched, /class="button button--danger" type="submit">Kalıcı silmeyi onayla/);
  const saving = renderProject(emptyProject, { states: [null, true, emptyProject.project.name], mutationPending: true });
  assert.match(saving, /class="button button--danger" type="submit" disabled="">Siliniyor…/);
  assert.match(saving, /id="project-delete-confirmation"[^>]*disabled=""/);
});

test("project detail loading, failure and not-found states have accessible recovery guidance", () => {
  const loading = renderProject(emptyProject, { queries: { "resource-access-project": pending() } });
  assert.match(loading, /role="status" lang="tr">Proje ayrıntıları yükleniyor/);
  const failed = renderProject(emptyProject, { queries: { "resource-access-project": failure() } });
  assert.match(failed, /role="alert" lang="tr"/);
  assert.match(failed, /Tekrar dene/);
  const missing = failure(); missing.error.status = 404;
  const notFound = renderProject(emptyProject, { queries: { "resource-access-project": missing } });
  assert.match(notFound, /Proje bulunamadı/);
  assert.match(notFound, /mevcut kurumunuzda bulunmuyor/);
  assert.match(notFound, /href="\/admin\/resource-access"/);
  assert.doesNotMatch(notFound, /Tekrar dene|Projeyi sil/);
});

test("project rename trims valid names, keeps API identifiers and rejects invalid names", async () => {
  const calls = [];
  const api = ui({ api: { apiPut: async (path, body) => { calls.push({ path, body }); } } })
    .load("lib/api/resource-access-projects");
  await api.renameAccessProject("project/a", "  Yeni Proje  ");
  assert.deepEqual(calls, [{ path: "/api/access/projects/project%2Fa", body: { name: "Yeni Proje" } }]);
  assert.throws(() => api.renameAccessProject("project-42", "  "), /1–160/);
  assert.throws(() => api.renameAccessProject("project-42", "a".repeat(161)), /1–160/);
  assert.equal(calls.length, 1);
});

test("project delete refuses default, target/grant bound or mismatched name before making a request", async () => {
  const calls = [];
  const api = ui({ api: { apiDelete: async path => { calls.push(path); } } }).load("lib/api/resource-access-projects");
  for (const data of [{ ...emptyProject, project: { ...emptyProject.project, isDefault: true } },
    { ...emptyProject, targets: populatedProject.targets }, { ...emptyProject, grants: populatedProject.grants }]) {
    await assert.rejects(api.deleteAccessProject(data, data.project.name));
  }
  await assert.rejects(api.deleteAccessProject(emptyProject, "Üretim projesi"), /tam olarak/);
  assert.equal(calls.length, 0);
  await api.deleteAccessProject(emptyProject, emptyProject.project.name);
  assert.deepEqual(calls, ["/api/access/projects/project-42"]);
});

test("project delete propagates authoritative server conflicts and never reports success on rejection", async () => {
  const conflict = Object.assign(new Error("Projeye yeni bir hedef bağlandı."), { status: 409 });
  const api = ui({ api: { apiDelete: async () => { throw conflict; } } }).load("lib/api/resource-access-projects");
  await assert.rejects(api.deleteAccessProject(emptyProject, emptyProject.project.name), error => error === conflict);
});

test("project detail reads cancelable API data and audit is filtered to the selected project", async () => {
  const queryConfigurations = [], calls = [];
  renderProject(emptyProject, { states: [null, false, "", false, null, true], queryConfigurations,
    api: { apiGet: async (path, init) => { calls.push({ path, init }); } } });
  const signal = new AbortController().signal;
  await queryConfigurations.find(item => item.queryKey[0] === "resource-access-project").queryFn({ signal });
  await queryConfigurations.find(item => item.queryKey[0] === "resource-access-audit").queryFn({ signal });
  assert.equal(calls[0].path, "/api/access/projects/project-42");
  assert.equal(calls[0].init.signal, signal);
  assert.equal(calls[1].path, "/api/access/audit?page=1&action=&entityType=project&entityId=project-42");
  assert.equal(calls[1].init.signal, signal);
});

test("project save refreshes directory, detail, team/audit and target caches without navigating away", async () => {
  const mutations = [], refreshed = [], navigated = [];
  renderProject(emptyProject, { states: ["Yeni Proje"], mutations,
    queryClient: { invalidateQueries: async options => { refreshed.push(options.queryKey); } },
    router: { replace: path => { navigated.push(path); } } });
  await mutations[0].onSuccess();
  for (const key of ["resource-access", "resource-access-team", "resource-access-audit", "resource-access-preview", "targets", "dashboard"])
    assert.ok(refreshed.some(item => item[0] === key), key);
  assert.ok(refreshed.some(item => item[0] === "resource-access-project" && item[1] === "project-42"));
  assert.deepEqual(navigated, []);
});

test("successful project deletion removes its cached detail and refreshes listing before navigation", async () => {
  const mutations = [], operations = [];
  renderProject(emptyProject, { states: [null, true, emptyProject.project.name], mutations,
    api: { apiDelete: async path => { operations.push(["delete", path]); } },
    queryClient: { removeQueries: options => { operations.push(["remove", options]); },
      invalidateQueries: async options => { operations.push(["refresh", options.queryKey]); } },
    router: { replace: path => { operations.push(["navigate", path]); } } });
  await mutations[1].mutationFn();
  await mutations[1].onSuccess();
  assert.deepEqual(operations[0], ["delete", "/api/access/projects/project-42"]);
  assert.deepEqual(operations[1], ["remove", { queryKey: ["resource-access-project", "project-42"], exact: true }]);
  assert.ok(operations.some(item => item[0] === "refresh" && item[1][0] === "resource-access"));
  assert.deepEqual(operations.at(-1), ["navigate", "/admin/resource-access"]);
  const html = renderProject(emptyProject, { states: [null, false, "", true] });
  assert.match(html, /role="status">Proje silindi/);
  assert.doesNotMatch(html, /Projeyi sil|Adı kaydet/);
});

test("rejected project deletion refreshes current guard information without removing cache or navigating", async () => {
  const mutations = [], refreshed = [], navigated = [], removed = [];
  renderProject(emptyProject, { states: [null], mutations,
    queryClient: { invalidateQueries: async options => { refreshed.push(options.queryKey); }, removeQueries: options => { removed.push(options); } },
    router: { replace: path => { navigated.push(path); } } });
  await mutations[1].onError(new Error("Kayıtlı ekip izinleri var."));
  assert.deepEqual(refreshed, [["resource-access-project", "project-42"]]);
  assert.deepEqual(navigated, []); assert.deepEqual(removed, []);
  const html = renderProject(emptyProject, { states: [null, false, "", false, { error: true, message: "Kayıtlı ekip izinleri var." }] });
  assert.match(html, /role="alert"/); assert.match(html, /Kayıtlı ekip izinleri var/);
});

test("project audit labels keep API action keys and existing team filtering", async () => {
  const fixture = ui(), labels = fixture.load("lib/api/resource-access").accessActionLabels;
  assert.equal(labels["project.renamed"], "Proje adı değiştirildi");
  assert.equal(labels["project.deleted"], "Proje silindi");
  const queryConfigurations = [], paths = [];
  ui({ queryConfigurations, api: { apiGet: async path => { paths.push(path); } } })
    .render("components/admin/resource-access-audit", "ResourceAccessAudit", { teamId: "team-31" });
  await queryConfigurations[0].queryFn({});
  assert.equal(paths[0], "/api/access/audit?page=1&action=&teamId=team-31");
});

test("resource access overview shows five recent audit records and links to the full history", () => {
  const records = Array.from({ length: 7 }, (_, index) => ({
    id: `audit-${index}`, timestamp: `2026-10-14T12:0${index}:00Z`, actorUsername: "dbscanner-admin",
    actorSubject: user.id, action: "team.created", entityType: "team", entityId: `team-${index}`,
    beforeJson: null, afterJson: JSON.stringify({ name: `Ekip ${index}` }),
  }));
  const html = ui({ queries: { "resource-access-audit": success({ items: records, totalCount: 12, page: 1, pageSize: 20 }) } })
    .render("components/admin/resource-access-client", "ResourceAccessClient");
  assert.match(html, /Son işlemler/);
  assert.match(html, /Tarama ve oturum olayları bu geçmişin kapsamında değildir/);
  assert.match(html, /href="\/admin\/resource-access\/audit"[^>]*>Tüm denetim geçmişini gör/);
  assert.match(html, /12 kayıt · Son 5 kayıt gösteriliyor/);
  const rows = html.match(/<tbody>([\s\S]*?)<\/tbody>/)?.[1] ?? "";
  assert.equal((rows.match(/<tr\b/g) ?? []).length, 5);
  assert.ok(rows.includes("team-0"));
  assert.ok(!rows.includes("team-5"));
});

test("full resource access audit page stays admin-only", () => {
  const denied = ui({ roles: ["dbscanner-viewer"] }).render("app/admin/resource-access/audit/page");
  assert.match(denied, /Erişim Engellendi/);
  assert.doesNotMatch(denied, /Tüm işlemler/);
  const admin = ui().render("app/admin/resource-access/audit/page");
  assert.match(admin, /Denetim geçmişi/);
  assert.match(admin, /href="\/admin\/resource-access"/);
});

const projectDirectory = {
  projects: [emptyProject.project, { id: "project-99", name: "Entegrasyon Projesi" }],
  teams: populatedProject.teams, members: [], grants: populatedProject.grants,
  targets: [...populatedProject.targets, { id: "target-18", name: "Entegrasyon MySQL", projectId: "project-99" }],
};

function renderProjectTargets(options = {}, overrides = {}) {
  return ui(options).render("components/admin/resource-access-project-targets", "ResourceAccessProjectTargets", {
    projectId: "project-42", targets: populatedProject.targets, directory: projectDirectory,
    directoryPending: false, directoryFetching: false, directoryError: null, onRetry() {}, disabled: false,
    onMove() {}, ...overrides,
  });
}

test("project targets separate own targets from the outside-target binding selector", () => {
  const html = renderProjectTargets();
  const list = html.match(/<ul[^>]*aria-label="Projeye bağlı hedefler"[^>]*>([\s\S]*?)<\/ul>/)[1];
  assert.match(list, /Üretim PostgreSQL/);
  assert.doesNotMatch(list, /Entegrasyon MySQL/);
  const binding = html.match(/<select id="project-bind-existing-target"[\s\S]*?<\/select>/)[0];
  assert.match(binding, /Entegrasyon MySQL · Entegrasyon Projesi/);
  assert.doesNotMatch(binding, /Üretim PostgreSQL/);
  assert.match(html, /hedefe özel eski ekip izinlerini kaldırır/);
  assert.match(html, /Tarama geçmişi korunur/);
  assert.match(html, /çalışan taraması olan hedef taşınamaz/);
});

test("project target movement and binding require explicit submit, not selection changes", () => {
  const elements = [], calls = [];
  renderProjectTargets({ elements, states: [{ "target-17": "project-99" }, "target-18"] }, {
    onMove: (targetId, destinationProjectId) => calls.push({ targetId, destinationProjectId }),
  });
  elements.find(element => element.type === "select" && element.props.id === "project-target-destination-target-17")
    .props.onChange({ target: { value: "project-99" } });
  assert.deepEqual(calls, []);
  for (const form of elements.filter(element => element.type === "form")) form.props.onSubmit({ preventDefault() {} });
  assert.deepEqual(calls, [{ targetId: "target-17", destinationProjectId: "project-99" },
    { targetId: "target-18", destinationProjectId: "project-42" }]);
});

test("stale targets/projects and refreshing/error directory never submit a move", () => {
  for (const overrides of [{ directory: { ...projectDirectory, projects: [emptyProject.project], targets: populatedProject.targets } },
    { directoryFetching: true }, { directoryError: new Error("Seçenekler alınamadı") }, { disabled: true }, { directory: undefined, directoryPending: true }]) {
    const elements = [], calls = [];
    renderProjectTargets({ elements, states: [{ "target-17": "project-99" }, "target-18"] }, { ...overrides, onMove: (...args) => calls.push(args) });
    for (const form of elements.filter(element => element.type === "form")) form.props.onSubmit({ preventDefault() {} });
    assert.deepEqual(calls, []);
  }
});

test("target move transport keeps target IDs and rejects empty inputs before sending", async () => {
  const calls = [];
  const api = ui({ api: { apiPut: async (path, body) => calls.push({ path, body }) } }).load("lib/api/resource-access-projects");
  await api.moveAccessTargetToProject("target/a", "project-99");
  assert.deepEqual(calls, [{ path: "/api/access/targets/target%2Fa/project", body: { projectId: "project-99" } }]);
  assert.throws(() => api.moveAccessTargetToProject("", "project-99"));
  assert.throws(() => api.moveAccessTargetToProject("target/a", " "));
  assert.equal(calls.length, 1);
});

test("project move stays within selected project, refreshes every affected cache and keeps server conflicts", async () => {
  const mutations = [], calls = [], refreshed = [];
  renderProject(populatedProject, { mutations, queries: { "resource-access": success(projectDirectory) },
    api: { apiPut: async (path, body) => calls.push({ path, body }) },
    queryClient: { invalidateQueries: async options => refreshed.push(options.queryKey) } });
  const move = mutations[2];
  await move.mutationFn({ targetId: "target-17", destinationProjectId: "project-99" });
  await move.mutationFn({ targetId: "target-18", destinationProjectId: "project-42" });
  assert.equal(calls.length, 2);
  for (const args of [{ targetId: "missing", destinationProjectId: "project-99" },
    { targetId: "target-17", destinationProjectId: "missing" },
    { targetId: "target-18", destinationProjectId: "project-99" }]) assert.throws(() => move.mutationFn(args));
  await move.onSuccess();
  for (const key of ["resource-access", "resource-access-project", "resource-access-team", "resource-access-audit", "resource-access-preview", "targets", "dashboard"])
    assert.ok(refreshed.some(item => item[0] === key), key);
  refreshed.length = 0;
  await move.onError(new Error("Aktif tarama sırasında taşınamaz."));
  assert.deepEqual(refreshed, [["resource-access"], ["resource-access-project", "project-42"]]);
  const html = renderProject(populatedProject, { states: [null, false, "", false, { error: true, message: "Aktif tarama sırasında taşınamaz." }] });
  assert.match(html, /role="alert"/); assert.match(html, /Aktif tarama sırasında taşınamaz/);
});

const teamDetails = {
  team: { id: "team-31", name: "Güvenlik Ekibi", isArchived: false },
  projects: projectDirectory.projects, targets: projectDirectory.targets,
  members: [{ teamId: "team-31", subject: user.id }],
  grants: populatedProject.grants.filter(grant => grant.teamId === "team-31"),
};

function renderTeamGrants(options = {}, overrides = {}) {
  return ui(options).render("components/admin/resource-access-team-grants", "ResourceAccessTeamGrants", {
    data: teamDetails, busy: false, onChange() {}, ...overrides,
  });
}

test("team detail owns real-user membership and permissions with lazy disclosures", () => {
  const queryConfigurations = [];
  const html = ui({ queryConfigurations, queries: { "resource-access-team": success(teamDetails) } })
    .render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "team-31" });
  for (const text of ["Güvenlik Ekibi", "Ekip bilgileri", "Üyeler", "keycloak-user-123", "Çağrı Yılmaz", "Keycloak kullanıcılarında ara", "Ekip izinleri", "İzin ekle", "Düzenle", "İzni kaldır"]) assert.ok(html.includes(text), text);
  assert.match(html, /id="team-permissions"/);
  assert.match(html, /href="#team-access-preview"/);
  assert.match(html, /<summary>Etkin erişim önizlemesi<\/summary>/);
  assert.match(html, /<summary>Değişiklik geçmişi<\/summary>/);
  assert.ok(!queryConfigurations.some(query => ["resource-access-preview", "resource-access-audit"].includes(query.queryKey[0])));
  assert.doesNotMatch(html, /<form[^>]*resource-access-grant-editor/);
});

test("team detail sends the selected users together to the batch membership endpoint", async () => {
  const secondUser = { ...user, id: "second-user-id", username: "grace" };
  const thirdUser = { ...user, id: "third-user-id", username: "katherine" };
  const calls = [], submittedActions = [];
  const elements = [];
  ui({ elements, submittedActions,
    states: [null, false, [secondUser, thirdUser], "", false, false, null, "", "", 1],
    queries: { "resource-access-team": success(teamDetails) },
    api: { apiPut: async (path, body) => { calls.push({ path, body }); return { addedCount: 2, alreadyMemberCount: 0 }; } },
  }).render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "team-31" });
  assert.ok(elements.some(element => element.type === "button" && element.props.children === "Seçilen 2 kullanıcıyı ekle"));
  const membershipForm = elements.filter(element => element.type === "form")[1];
  membershipForm.props.onSubmit({ preventDefault() {} });
  assert.equal(submittedActions.length, 1);
  await submittedActions[0].variables();
  assert.deepEqual(calls, [{ path: "/api/access/teams/team-31/members/batch", body: { subjects: [secondUser.id, thirdUser.id] } }]);
});

test("new team grant fixes team scope, supports whole-project/target permissions and always requires Read", async () => {
  for (const targetId of ["", "target-17"]) {
    const elements = [], actions = [], calls = [];
    const html = renderTeamGrants({ elements, states: [true, null, "project-42", targetId, 10],
      api: { apiPut: async (path, body) => calls.push({ path, body }) },
    }, { onChange: action => actions.push(action) });
    assert.match(html, /Yeni proje veya hedef izni/);
    assert.doesNotMatch(html, /<label[^>]*>Ekip<\/label>/);
    const form = elements.find(element => element.type === "form");
    form.props.onSubmit({ preventDefault() {} });
    await actions[0]();
    assert.deepEqual(calls, [{ path: "/api/access/grants", body: { teamId: "team-31", projectId: "project-42", targetId: targetId || null, permissions: 11 } }]);
  }
});

test("existing grant editor preserves scope and prepopulated permissions instead of creating a different grant", async () => {
  const elements = [], actions = [], calls = [];
  const html = renderTeamGrants({ elements, states: [true, "grant-2", "project-42", "target-17", 5],
    api: { apiPut: async (path, body) => calls.push({ path, body }) },
  }, { onChange: action => actions.push(action) });
  assert.match(html, /İzni düzenle/); assert.match(html, /Kapsamı değiştirmek için izni kaldırıp yeni izin ekleyin/);
  for (const select of elements.filter(element => element.type === "select")) assert.equal(select.props.disabled, true);
  assert.match(html, /value="target-17" selected=""/);
  elements.find(element => element.type === "form").props.onSubmit({ preventDefault() {} });
  await actions[0]();
  assert.deepEqual(calls[0].body, { teamId: "team-31", projectId: "project-42", targetId: "target-17", permissions: 5 });
});

test("removed project, moved target, removed grant and archived/busy team block stale grant submission", () => {
  const cases = [
    { states: [true, null, "missing-project", "", 3] },
    { states: [true, null, "project-42", "target-18", 3] },
    { states: [true, "removed-grant", "project-42", "", 3] },
    { states: [true, "grant-2", "project-99", "target-18", 5] },
    { states: [true, null, "project-42", "", 3], busy: true },
    { states: [true, null, "project-42", "", 3], data: { ...teamDetails, team: { ...teamDetails.team, isArchived: true } } },
  ];
  for (const { states, ...overrides } of cases) {
    const elements = [], actions = [];
    renderTeamGrants({ elements, states }, { ...overrides, onChange: action => actions.push(action) });
    const form = elements.find(element => element.type === "form");
    if (form) form.props.onSubmit({ preventDefault() {} });
    assert.deepEqual(actions, []);
  }
  const staleProject = renderTeamGrants({ states: [true, null, "missing-project", "", 3] });
  assert.match(staleProject, /Seçilen proje artık mevcut değil/);
  const staleTarget = renderTeamGrants({ states: [true, null, "project-42", "target-18", 3] });
  assert.match(staleTarget, /Seçilen hedef artık bu projede değil/);
});

test("archived team can revoke retained grants but cannot create or edit them", async () => {
  const elements = [], actions = [], calls = [];
  const html = renderTeamGrants({ elements, api: { apiDelete: async path => calls.push(path) } }, {
    data: { ...teamDetails, team: { ...teamDetails.team, isArchived: true } }, onChange: action => actions.push(action),
  });
  assert.doesNotMatch(html, />İzin ekle</);
  assert.match(html, /Mevcut izinleri kaldırabilirsiniz/);
  assert.ok(elements.filter(element => element.type === "button" && element.props.children === "Düzenle").every(element => element.props.disabled));
  elements.find(element => element.type === "button" && element.props.children === "İzni kaldır").props.onClick();
  await actions[0]();
  assert.deepEqual(calls, ["/api/access/grants/grant-1"]);
});

test("team mutations refresh scoped views and refuse updates while refreshing", async () => {
  const mutations = [], refreshed = [];
  ui({ mutations, queries: { "resource-access-team": success(teamDetails) },
    queryClient: { invalidateQueries: async options => refreshed.push(options.queryKey) },
  }).render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "team-31" });
  await mutations[0].onSuccess();
  for (const key of ["resource-access", "resource-access-team", "resource-access-project", "resource-access-audit", "resource-access-preview", "targets", "dashboard"])
    assert.ok(refreshed.some(item => item[0] === key), key);
  const refreshing = [], actions = [];
  ui({ mutations: refreshing, queries: { "resource-access-team": { ...success(teamDetails), isFetching: true } } })
    .render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "team-31" });
  assert.throws(() => refreshing[0].mutationFn(() => actions.push("unexpected")), /yenileniyor/);
  assert.deepEqual(actions, []);
});

test("project history is lazy and team preview/history mount only when opened", () => {
  const closedQueries = [];
  renderProject(emptyProject, { queryConfigurations: closedQueries });
  assert.ok(!closedQueries.some(query => query.queryKey[0] === "resource-access-audit"));
  const openedQueries = [];
  ui({ queryConfigurations: openedQueries, states: [null, false, [], user.id, true, true, null, "", "", 1], queries: { "resource-access-team": success(teamDetails) } })
    .render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "team-31" });
  assert.ok(openedQueries.some(query => query.queryKey[0] === "resource-access-preview"));
  assert.ok(openedQueries.some(query => query.queryKey[0] === "resource-access-audit"));
});

test("archived team explains retained memberships/grants in Turkish", () => {
  const data = { team: { id: "t1", name: "Güvenlik", isArchived: true }, members: [], grants: [], projects: [], targets: [] };
  const html = ui({ queries: { "resource-access-team": success(data) } })
    .render("components/admin/resource-access-team-detail", "ResourceAccessTeamDetail", { teamId: "t1" });
  assert.match(html, /Güvenlik/);
  assert.match(html, /Üyelikler ve izinler korunur/);
  assert.match(html, /Ekibi yeniden etkinleştir/);
  assert.match(html, /lang="tr"/);
});

test("preview translates role/permission labels without replacing decisions", () => {
  const data = { user, target: { id: "target1", name: "Production" }, sources: [], basis: "Based on current role and grants.",
    decisions: [{ permission: 1, allowed: true, reason: "Administrator access." },
      { permission: 8, allowed: false, reason: "No matching grant." }] };
  const html = ui({ queries: { "resource-access-preview": success(data) } }).render("components/admin/resource-access-preview", "ResourceAccessPreview", { subject: user.id, targets: [data.target] });
  for (const text of ["Etkin erişim önizlemesi", "Yönetici", "İzinli", "İzinsiz", "No matching grant."]) assert.ok(html.includes(text), text);
  assert.match(html, /lang="tr"/);
});

test("audit display translates action labels without changing stored keys", () => {
  const record = { id: "audit1", timestamp: "2026-10-11T12:00:00Z", actorUsername: user.username,
    actorSubject: user.id, action: "team.created", entityId: "t1", beforeJson: null,
    afterJson: JSON.stringify({ name: "Güvenlik" }) };
  const html = ui({ queries: { "resource-access-audit": success(page([record])) } })
    .render("components/admin/resource-access-audit", "ResourceAccessAudit");
  assert.match(html, /Denetim geçmişi/);
  assert.match(html, /value="team.created"/);
  assert.match(html, /Önce/);
  assert.match(html, /Ekip oluşturuldu/);
  assert.match(html, /Güvenlik/);
  assert.match(html, /keycloak-user-123/);
  assert.match(html, /lang="tr"/);
  assert.ok(html.includes(new Date(record.timestamp).toLocaleString("tr-TR")));
});

test("audit empty state explains its scope and history in Turkish", () => {
  assert.match(ui().render("components/admin/resource-access-audit", "ResourceAccessAudit"), /Bu kapsamda henüz denetim kaydı yok/);
});

test("toast close action has a Turkish accessible label", () => {
  const html = ui().render("components/feedback/operation-toast", "OperationToast", { message: "Kaydedildi.", variant: "success", onClose: () => {} });
  assert.match(html, /aria-label="Bildirimi kapat"/);
});

test("temporary-password creation and role update keep original API payloads", async () => {
  const calls = [];
  const record = async (path, body) => { calls.push({ path, body }); return {}; };
  const api = ui({ api: { apiPost: record, apiPut: record } }).load("lib/api/admin-users");
  const request = { username: "test-user", role: "dbscanner-viewer", requireMfa: true, temporaryPassword: "test-only-password" };
  await api.createAdminUser(request);
  await api.updateAdminUserRole(user.id, adminRole);
  assert.deepEqual(calls, [{ path: "/api/admin/users", body: request },
    { path: `/api/admin/users/${user.id}/roles`, body: { role: adminRole } }]);
});
