import { expect, test } from '@playwright/test'
import { createServer, request as proxyRequest } from 'node:http'
import type { AddressInfo } from 'node:net'

for (const width of [320, 1440]) {
  for (const colorScheme of ['light', 'dark'] as const) {
    test(`${width}px ${colorScheme}: native EventSource resumes cursor and completes exactly once`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await page.addInitScript(() =>
        sessionStorage.setItem('whaledeck-preview-authenticated', 'true'),
      )
      const job = {
        id: '7b5dbcea-3eb6-414d-ac67-0308a4137ccc',
        jobType: 'native.stream',
        state: 'Running',
        phase: 'Queued',
        progressPercent: 0,
        errorCode: null,
        createdAtUtc: '2026-10-16T01:00:00Z',
        completedAtUtc: null,
        resultJson: null,
      }
      let opens = 0
      const cursors: (string | undefined)[] = []
      const writes: string[] = []
      // Use actual loopback HTTP, not Fetch interception: Chromium adds the
      // reconnect cursor after the interception snapshot in some versions.
      const server = createServer((request, response) => {
        const url = new URL(request.url!, 'http://127.0.0.1')
        if (!url.pathname.startsWith('/api/v1/')) {
          const upstream = proxyRequest(
            new URL(request.url!, 'http://127.0.0.1:5174'),
            { method: request.method },
            (incoming) => {
              response.writeHead(incoming.statusCode ?? 502, incoming.headers)
              incoming.pipe(response)
            },
          )
          upstream.on('error', () => {
            response.writeHead(502)
            response.end()
          })
          request.pipe(upstream)
          return
        }
        const path = url.pathname.replace('/api/v1/', '')
        const json = (value: unknown) => {
          response.setHeader('Content-Type', 'application/json')
          response.end(JSON.stringify(value))
        }
        if (request.method !== 'GET') writes.push(path)
        if (path === `jobs/${job.id}/events`) {
          opens++
          cursors.push(request.headers['last-event-id'] as string | undefined)
          const first = {
            Sequence: 1,
            State: 'Running',
            Phase: '开始执行',
            ProgressPercent: 10,
            MessageCode: 'STREAM_STARTED',
            OccurredAtUtc: job.createdAtUtc,
          }
          const last = {
            sequence: 2,
            state: 'Succeeded',
            phase: '任务已完成',
            progressPercent: 100,
            messageCode: 'STREAM_DONE',
            occurredAtUtc: job.createdAtUtc,
          }
          if (opens >= 2) {
            job.state = 'Succeeded'
            job.phase = last.phase
            job.progressPercent = 100
          }
          response.setHeader('Content-Type', 'text/event-stream')
          response.end(
            opens === 1
              ? `retry: 100\nid: 1\nevent: progress\ndata: ${JSON.stringify(first)}\n\n`
              : `id: 1\nevent: progress\ndata: ${JSON.stringify(first)}\n\nid: 2\nevent: progress\ndata: ${JSON.stringify(last)}\n\n`,
          )
          return
        }
        if (path === 'jobs') return json([job])
        if (path === `jobs/${job.id}`) return json(job)
        json(path.startsWith('notifications') ? { items: [], unreadCount: 0 } : [])
      })
      await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
      try {
        await page.goto(`http://127.0.0.1:${(server.address() as AddressInfo).port}/jobs`)
        await page.locator('.jobs-page .el-table__body tr').click()
        const drawer = page.locator('.el-drawer')
        await expect(drawer.locator('.el-timeline-item')).toHaveCount(2)
        await expect(
          drawer.locator('.el-timeline-item').getByText('任务已完成', { exact: true }),
        ).toBeVisible()
        await expect(drawer.getByRole('button', { name: '取消任务' })).toHaveCount(0)
        expect(cursors).toEqual([undefined, '1'])
        expect(writes).toEqual([])
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
          true,
        )
        await page.screenshot({
          path: `test-results/job-events-${width}-${colorScheme}.png`,
          scale: 'css',
        })
        expect(errors).toEqual([])
      } finally {
        server.closeAllConnections()
        await new Promise<void>((resolve, reject) =>
          server.close((error) => (error ? reject(error) : resolve())),
        )
      }
    })
  }
}
