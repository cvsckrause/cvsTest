import * as cdk from 'aws-cdk-lib'
import { Template } from 'aws-cdk-lib/assertions'
import assert from 'node:assert/strict'
import { ErpDevStack } from '../lib/erp-dev-stack'

const app = new cdk.App()
const stack = new ErpDevStack(app, 'Erp-dev', {
  environment: 'dev',
  env: { region: 'eu-north-1' },
})
const template = Template.fromStack(stack)

template.hasResourceProperties('AWS::CloudWatch::Alarm', {
  AlarmName: 'erp-dev-outbox-dead-letter',
  Namespace: 'Erp/Outbox',
  MetricName: 'DeadLetterCount',
  ComparisonOperator: 'GreaterThanThreshold',
  Threshold: 0,
  Dimensions: [{ Name: 'Service', Value: 'worker' }],
  TreatMissingData: 'notBreaching',
})

template.hasResourceProperties('AWS::CloudWatch::Alarm', {
  AlarmName: 'erp-dev-outbox-oldest-pending',
  Namespace: 'Erp/Outbox',
  MetricName: 'OldestPendingAgeSeconds',
  ComparisonOperator: 'GreaterThanThreshold',
  Threshold: 900,
  Dimensions: [{ Name: 'Service', Value: 'worker' }],
  TreatMissingData: 'notBreaching',
})

const alarms = template.findResources('AWS::CloudWatch::Alarm')
assert.ok(Object.keys(alarms).length >= 2)
console.log('outbox alarms ok')
