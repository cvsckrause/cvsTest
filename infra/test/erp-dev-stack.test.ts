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

const client = JSON.stringify(template.findResources('AWS::Cognito::UserPoolClient'))
assert.match(client, /ALLOW_USER_SRP_AUTH/)
assert.match(client, /ALLOW_ADMIN_USER_PASSWORD_AUTH/)
assert.match(client, /"ALLOW_USER_PASSWORD_AUTH"/)
assert.doesNotMatch(client, /CallbackURLs|AllowedOAuthFlows"/)
assert.equal(Object.keys(template.findResources('AWS::Cognito::UserPoolDomain')).length, 0)
assert.ok(template.findOutputs('AlbDnsName').AlbDnsName)
console.log('smoke auth and alb output ok')

const apiTaskDefinition = Object.values(template.findResources('AWS::ECS::TaskDefinition')).find((resource) =>
  JSON.stringify(resource).includes('"Name":"Api"'),
)
assert.ok(apiTaskDefinition, 'API task definition is missing')
const apiEnvironment = JSON.stringify(apiTaskDefinition)
for (const name of [
  'Registration__AwsRegion',
  'Registration__UserPoolId',
  'Registration__ApproverEmail',
  'Registration__FromEmail',
  'Registration__PublicBaseUrl',
  'ForwardedHeaders__ForwardLimit',
]) {
  assert.match(apiEnvironment, new RegExp(`"Name":"${name}"`), `${name} is not set on the API task`)
}
assert.match(apiEnvironment, /"Value":"ckrause@cvs\.de"/)

const registrationPolicy = JSON.stringify(template.findResources('AWS::IAM::Policy'))
for (const action of ['AdminCreateUser', 'AdminSetUserPassword', 'AdminGetUser']) {
  assert.match(registrationPolicy, new RegExp(`cognito-idp:${action}`))
}
assert.match(registrationPolicy, /"ses:SendEmail"/)
assert.match(registrationPolicy, /"ses:FromAddress":"ckrause@cvs\.de"/)
assert.equal(Object.keys(template.findResources('AWS::SES::EmailIdentity')).length, 0)
console.log('registration wiring ok')

const digest = 'sha256:1111111111111111111111111111111111111111111111111111111111111111'
const pinnedApp = new cdk.App()
pinnedApp.node.setContext('apiImageDigest', digest)
pinnedApp.node.setContext('workerImageDigest', digest)
pinnedApp.node.setContext('migratorImageDigest', digest)
const pinned = new ErpDevStack(pinnedApp, 'Erp-dev', {
  environment: 'dev',
  env: { account: '098981324459', region: 'eu-north-1' },
})
const pinnedTemplate = Template.fromStack(pinned)
const pinnedTasks = JSON.stringify(pinnedTemplate.findResources('AWS::ECS::TaskDefinition'))
assert.match(pinnedTasks, new RegExp(`@${digest}`))
assert.equal((pinnedTasks.match(new RegExp(digest, 'g')) ?? []).length, 3)
const policies = JSON.stringify(pinnedTemplate.findResources('AWS::IAM::Policy'))
assert.match(policies, /ecr:BatchGetImage/)
console.log('pinned image digests ok')
