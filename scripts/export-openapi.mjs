import { spawn } from 'node:child_process'
import { mkdir, writeFile } from 'node:fs/promises'
import http from 'node:http'
import path from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const outDir = path.join(root, '.artifacts', 'openapi-api')
const port = 5088
const documentUrl = `http://127.0.0.1:${port}/openapi/v1.json`

function run(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: root, stdio: 'inherit' })
    child.on('error', reject)
    child.on('exit', (code) => {
      if (code === 0) resolve()
      else reject(new Error(`${command} ${args.join(' ')} exited ${code}`))
    })
  })
}

function getJson(url) {
  return new Promise((resolve, reject) => {
    const request = http.get(url, (response) => {
      const chunks = []
      response.on('data', (chunk) => chunks.push(chunk))
      response.on('end', () => {
        const body = Buffer.concat(chunks).toString('utf8')
        if (response.statusCode !== 200) {
          reject(new Error(`OpenAPI request failed with ${response.statusCode}`))
          return
        }
        resolve(JSON.parse(body))
      })
    })
    request.setTimeout(2000, () => {
      request.destroy(new Error('OpenAPI request timed out'))
    })
    request.on('error', reject)
  })
}

function sortKeys(value) {
  if (Array.isArray(value)) return value.map(sortKeys)
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .map((key) => [key, sortKeys(value[key])]),
    )
  }
  return value
}

await run('dotnet', [
  'build',
  'src/Erp.Api/Erp.Api.csproj',
  '-c',
  'Release',
  '-o',
  outDir,
  '--verbosity',
  'quiet',
])

const child = spawn('dotnet', ['Erp.Api.dll'], {
  cwd: outDir,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
    ConnectionStrings__Erp: 'Host=localhost;Port=5432;Database=erp_dev;Username=erp;Password=erp',
  },
  stdio: ['ignore', 'pipe', 'pipe'],
})

let log = ''
child.stdout?.on('data', (chunk) => {
  log += chunk
})
child.stderr?.on('data', (chunk) => {
  log += chunk
})

try {
  let document = null
  for (let attempt = 0; attempt < 40; attempt += 1) {
    if (child.exitCode !== null) {
      throw new Error(`API exited ${child.exitCode}\n${log}`)
    }
    try {
      document = await getJson(documentUrl)
      break
    } catch {
      // The listener is not up yet.
    }
    await delay(250)
  }

  if (!document) throw new Error(`OpenAPI endpoint did not respond\n${log}`)

  document.servers = [{ url: '/' }]
  await mkdir(path.join(root, 'openapi'), { recursive: true })
  await writeFile(
    path.join(root, 'openapi', 'v1.json'),
    `${JSON.stringify(sortKeys(document), null, 2)}\n`,
  )
} finally {
  child.kill()
}
