export type BuildInfo = {
  version: string
  buildTime: string
  gitSha: string
  label: string
}

export function getBuildInfo(
  env: Pick<ImportMetaEnv, 'VITE_APP_VERSION' | 'VITE_APP_BUILD_TIME' | 'VITE_APP_GIT_SHA'> = import.meta.env,
): BuildInfo {
  const version = trimOr(env.VITE_APP_VERSION, 'dev')
  const buildTime = trimOr(env.VITE_APP_BUILD_TIME, 'local')
  const gitSha = trimOr(env.VITE_APP_GIT_SHA, 'unknown')
  return {
    version,
    buildTime,
    gitSha,
    label: formatBuildStamp(version, buildTime, gitSha),
  }
}

export function formatBuildStamp(version: string, buildTime: string, gitSha: string): string {
  const normalizedVersion = version.startsWith('v') ? version : `v${version}`
  return `${normalizedVersion} · ${formatBuildTime(buildTime)} · ${gitSha}`
}

function formatBuildTime(raw: string): string {
  if (!raw || raw === 'local') return 'local'
  const parsed = Date.parse(raw)
  if (Number.isNaN(parsed)) return raw
  const iso = new Date(parsed).toISOString()
  return `${iso.slice(0, 16).replace('T', ' ')} UTC`
}

function trimOr(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim()
  return trimmed ? trimmed : fallback
}
