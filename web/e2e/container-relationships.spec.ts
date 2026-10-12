import { expect, test } from '@playwright/test'
import { relationshipResponse } from './fixtures/relationships.mjs'

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', (route) =>
    route.fulfill({ json: relationshipResponse(new URL(route.request().url()).pathname) }),
  )
})

for (const width of [320, 390, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width}px ${colorScheme}: relationships remain readable and precisely navigable`, async ({
      page,
    }) => {
      const errors: string[] = []
      page.on('pageerror', (error) => errors.push(error.message))
      await page.setViewportSize({ width, height: 1000 })
      await page.emulateMedia({ colorScheme })
      await page.goto('/containers')
      await page
        .locator('.container-card')
        .filter({ hasText: 'demo-web' })
        .getByRole('button', { name: '详情', exact: true })
        .click()
      const relations = page.getByRole('region', { name: '关联资源' })
      await expect(relations.getByText('172.19.0.2/24', { exact: false })).toBeVisible()
      await expect(relations.getByText('仅容器声明，未发布到宿主机')).toBeVisible()
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      ).toBeTruthy()
      expect(
        await relations.evaluate((element) => element.scrollWidth <= element.clientWidth),
      ).toBeTruthy()
      await relations.getByRole('link', { name: 'team-net', exact: true }).click()
      await expect(page).toHaveURL(/\/docker\/networks\?resourceId=network/)
      await expect(page.getByRole('link', { name: '关联容器', exact: true })).toBeVisible()
      await page.getByRole('link', { name: '关联容器', exact: true }).click()
      await expect(page.locator('.container-card')).toHaveCount(1)
      await expect(page.locator('.container-card')).toContainText('demo-web')
      await page.getByRole('link', { name: '显示全部', exact: true }).click()
      await expect(page.locator('.container-card')).toHaveCount(2)
      expect(errors).toEqual([])
    })

test('deleted reference is explicit and reset restores resources', async ({ page }) => {
  await page.goto('/docker/volumes?resourceId=volume%3Adeleted')
  await expect(
    page.getByText('关联资源不存在或已被移除，请显示全部或返回刷新容器详情'),
  ).toBeVisible()
  await page.getByRole('link', { name: '显示全部', exact: true }).click()
  await expect(page.getByText('team-data', { exact: true })).toBeVisible()
})

test('permission failure clears the loaded resource row', async ({ page }) => {
  await page.goto('/docker/networks')
  await expect(page.getByText('team-net', { exact: true })).toBeVisible()
  await page.route('**/api/v1/networks', (route) =>
    route.fulfill({ status: 403, json: { title: '管理权限已失效', status: 403 } }),
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect(page.getByText('team-net', { exact: true })).toHaveCount(0)
  await expect(page.getByText('管理权限已失效', { exact: true })).toBeVisible()
})

test('image, volume and Compose links use exact identities and unregistered projects stay read-only', async ({
  page,
}) => {
  await page.goto('/containers')
  for (const [label, destination] of [
    ['nginx:alpine', '/docker/images'],
    ['team-data', '/docker/volumes'],
    ['team', '/compose-projects'],
  ]) {
    await page
      .locator('.container-card')
      .filter({ hasText: 'demo-web' })
      .getByRole('button', { name: '详情', exact: true })
      .click()
    await page
      .getByRole('region', { name: '关联资源' })
      .getByRole('link', { name: label, exact: true })
      .click()
    await expect(page).toHaveURL(new RegExp(destination + '\\?'))
    if (destination === '/compose-projects') {
      await expect(page.locator('.compose-project')).toHaveCount(1)
      await expect(page.locator('.compose-project button')).toHaveCount(0)
      await expect(page.getByRole('link', { name: '应用详情', exact: true })).toHaveCount(0)
    }
    await page.getByRole('link', { name: '关联容器', exact: true }).click()
    await expect(page.locator('.container-card')).toHaveCount(1)
    await expect(page.locator('.container-card')).toContainText('demo-web')
  }
})

test('only a registered ComposeApplication offers application details', async ({ page }) => {
  await page.route('**/api/v1/compose-projects', (route) =>
    route.fulfill({
      json: [
        {
          id: 'application:demo',
          name: 'demo',
          type: 'ComposeApplication',
          state: 'Running',
          version: 'nginx:alpine',
          isProtected: false,
          attributes: {
            composeProject: 'whaledeck-app-demo',
            services: 'web',
            containerCount: '1',
            allowedActions: '',
          },
        },
      ],
    }),
  )
  await page.goto('/compose-projects?project=whaledeck-app-demo')
  await expect(page.locator('.compose-project')).toHaveCount(1)
  await expect(page.getByRole('link', { name: '应用详情', exact: true })).toHaveAttribute(
    'href',
    '/applications/application%3Ademo',
  )
})
