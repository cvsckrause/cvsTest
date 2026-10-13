#!/usr/bin/env node
import 'source-map-support/register'
import * as cdk from 'aws-cdk-lib'
import { ErpDevStack } from '../lib/erp-dev-stack'

const app = new cdk.App()
const region = app.node.tryGetContext('region') as string ?? 'eu-north-1'
const environment = app.node.tryGetContext('environment') as string ?? 'dev'

new ErpDevStack(app, `Erp-${environment}`, {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region,
  },
  description: 'Cost-limited ERP development cell. Not production shaped.',
  environment,
})
