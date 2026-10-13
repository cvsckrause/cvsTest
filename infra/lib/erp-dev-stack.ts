import * as cdk from 'aws-cdk-lib'
import * as budgets from 'aws-cdk-lib/aws-budgets'
import * as cognito from 'aws-cdk-lib/aws-cognito'
import * as ec2 from 'aws-cdk-lib/aws-ec2'
import * as ecrAssets from 'aws-cdk-lib/aws-ecr-assets'
import * as ecs from 'aws-cdk-lib/aws-ecs'
import * as elbv2 from 'aws-cdk-lib/aws-elasticloadbalancingv2'
import * as events from 'aws-cdk-lib/aws-events'
import * as kms from 'aws-cdk-lib/aws-kms'
import * as logs from 'aws-cdk-lib/aws-logs'
import * as rds from 'aws-cdk-lib/aws-rds'
import * as s3 from 'aws-cdk-lib/aws-s3'
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager'
import * as sqs from 'aws-cdk-lib/aws-sqs'
import { Construct } from 'constructs'
import * as path from 'node:path'

export interface ErpDevStackProps extends cdk.StackProps {
  environment: string
}

export class ErpDevStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: ErpDevStackProps) {
    super(scope, id, props)

    const environmentName = props.environment
    const monthlyBudgetUsd = Number(this.node.tryGetContext('monthlyBudgetUsd') ?? 50)
    const repoRoot = path.join(__dirname, '..', '..')

    cdk.Tags.of(this).add('Project', 'cvs-erp')
    cdk.Tags.of(this).add('Environment', environmentName)
    cdk.Tags.of(this).add('Owner', 'awscli-cvstest')
    cdk.Tags.of(this).add('CostCenter', 'erp-dev-cell')

    const vpc = new ec2.Vpc(this, 'Vpc', {
      maxAzs: 2,
      natGateways: 0,
      subnetConfiguration: [
        {
          name: 'public',
          subnetType: ec2.SubnetType.PUBLIC,
          cidrMask: 24,
        },
      ],
    })

    const key = new kms.Key(this, 'DataKey', {
      enableKeyRotation: true,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const documents = new s3.Bucket(this, 'Documents', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.KMS,
      encryptionKey: key,
      enforceSSL: true,
      versioned: true,
      autoDeleteObjects: true,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      lifecycleRules: [{ expiration: cdk.Duration.days(30) }],
    })

    const databaseSecret = new secretsmanager.Secret(this, 'DatabaseSecret', {
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ username: 'erp' }),
        generateStringKey: 'password',
        excludePunctuation: true,
        passwordLength: 32,
      },
      encryptionKey: key,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const databaseSecurityGroup = new ec2.SecurityGroup(this, 'DatabaseSecurityGroup', {
      vpc,
      description: 'PostgreSQL access from ERP Fargate tasks',
      allowAllOutbound: false,
    })

    const database = new rds.DatabaseInstance(this, 'Database', {
      engine: rds.DatabaseInstanceEngine.postgres({
        version: rds.PostgresEngineVersion.VER_16,
      }),
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T4G, ec2.InstanceSize.MICRO),
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroups: [databaseSecurityGroup],
      credentials: rds.Credentials.fromSecret(databaseSecret),
      databaseName: 'erp_dev',
      allocatedStorage: 20,
      maxAllocatedStorage: 40,
      storageEncrypted: true,
      storageEncryptionKey: key,
      multiAz: false,
      publiclyAccessible: false,
      deletionProtection: false,
      backupRetention: cdk.Duration.days(1),
      deleteAutomatedBackups: true,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
      cloudwatchLogsExports: ['postgresql'],
      cloudwatchLogsRetention: logs.RetentionDays.ONE_WEEK,
    })

    const userPool = new cognito.UserPool(this, 'UserPool', {
      selfSignUpEnabled: false,
      signInAliases: { email: true, username: true },
      passwordPolicy: {
        minLength: 12,
        requireDigits: true,
        requireLowercase: true,
        requireSymbols: true,
        requireUppercase: true,
      },
      mfa: cognito.Mfa.OPTIONAL,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const userPoolClient = userPool.addClient('SpaClient', {
      generateSecret: false,
      authFlows: { userSrp: true },
      oAuth: {
        flows: { authorizationCodeGrant: true },
        scopes: [cognito.OAuthScope.OPENID, cognito.OAuthScope.EMAIL, cognito.OAuthScope.PROFILE],
        callbackUrls: ['http://localhost:5173/', 'http://127.0.0.1:5173/'],
        logoutUrls: ['http://localhost:5173/', 'http://127.0.0.1:5173/'],
      },
      preventUserExistenceErrors: true,
    })

    const eventBus = new events.EventBus(this, 'IntegrationBus', {
      eventBusName: `erp-${environmentName}`,
    })

    const deadLetterQueue = new sqs.Queue(this, 'WorkerDlq', {
      retentionPeriod: cdk.Duration.days(14),
      encryption: sqs.QueueEncryption.KMS,
      encryptionMasterKey: key,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const workerQueue = new sqs.Queue(this, 'WorkerQueue', {
      visibilityTimeout: cdk.Duration.seconds(60),
      retentionPeriod: cdk.Duration.days(4),
      deadLetterQueue: {
        queue: deadLetterQueue,
        maxReceiveCount: 5,
      },
      encryption: sqs.QueueEncryption.KMS,
      encryptionMasterKey: key,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const cluster = new ecs.Cluster(this, 'Cluster', {
      vpc,
      containerInsightsV2: ecs.ContainerInsights.DISABLED,
    })

    const apiImage = ecs.ContainerImage.fromAsset(repoRoot, {
      file: 'src/Erp.Api/Dockerfile',
      platform: ecrAssets.Platform.LINUX_AMD64,
    })
    const workerImage = ecs.ContainerImage.fromAsset(repoRoot, {
      file: 'src/Erp.Worker/Dockerfile',
      platform: ecrAssets.Platform.LINUX_AMD64,
    })
    const migratorImage = ecs.ContainerImage.fromAsset(repoRoot, {
      file: 'src/Erp.Migrator/Dockerfile',
      platform: ecrAssets.Platform.LINUX_AMD64,
    })

    const logGroup = new logs.LogGroup(this, 'ApplicationLogs', {
      retention: logs.RetentionDays.ONE_WEEK,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    })

    const databaseEnv = {
      Database__Host: database.instanceEndpoint.hostname,
      Database__Name: 'erp_dev',
      Database__Username: 'erp',
      Worker__TenantId: '11111111-1111-1111-1111-111111111111',
      DevelopmentIdentity__TenantId: '11111111-1111-1111-1111-111111111111',
      DevelopmentIdentity__UserId: 'local-developer',
    }

    const apiTask = new ecs.FargateTaskDefinition(this, 'ApiTask', {
      cpu: 256,
      memoryLimitMiB: 512,
      runtimePlatform: {
        cpuArchitecture: ecs.CpuArchitecture.X86_64,
        operatingSystemFamily: ecs.OperatingSystemFamily.LINUX,
      },
    })
    const apiContainer = apiTask.addContainer('Api', {
      image: apiImage,
      logging: ecs.LogDrivers.awsLogs({ logGroup, streamPrefix: 'api' }),
      environment: databaseEnv,
      secrets: {
        Database__Password: ecs.Secret.fromSecretsManager(databaseSecret, 'password'),
      },
      portMappings: [{ containerPort: 8080 }],
    })
    apiContainer.addUlimits({
      name: ecs.UlimitName.NOFILE,
      softLimit: 1024,
      hardLimit: 2048,
    })

    const workerTask = new ecs.FargateTaskDefinition(this, 'WorkerTask', {
      cpu: 256,
      memoryLimitMiB: 512,
      runtimePlatform: {
        cpuArchitecture: ecs.CpuArchitecture.X86_64,
        operatingSystemFamily: ecs.OperatingSystemFamily.LINUX,
      },
    })
    workerTask.addContainer('Worker', {
      image: workerImage,
      logging: ecs.LogDrivers.awsLogs({ logGroup, streamPrefix: 'worker' }),
      environment: {
        ...databaseEnv,
        WorkerQueueUrl: workerQueue.queueUrl,
        EventBusName: eventBus.eventBusName,
        DocumentsBucket: documents.bucketName,
      },
      secrets: {
        Database__Password: ecs.Secret.fromSecretsManager(databaseSecret, 'password'),
      },
    })

    const migratorTask = new ecs.FargateTaskDefinition(this, 'MigratorTask', {
      cpu: 256,
      memoryLimitMiB: 512,
      runtimePlatform: {
        cpuArchitecture: ecs.CpuArchitecture.X86_64,
        operatingSystemFamily: ecs.OperatingSystemFamily.LINUX,
      },
    })
    migratorTask.addContainer('Migrator', {
      image: migratorImage,
      logging: ecs.LogDrivers.awsLogs({ logGroup, streamPrefix: 'migrator' }),
      environment: databaseEnv,
      secrets: {
        Database__Password: ecs.Secret.fromSecretsManager(databaseSecret, 'password'),
      },
    })

    documents.grantReadWrite(apiTask.taskRole)
    documents.grantReadWrite(workerTask.taskRole)
    workerQueue.grantConsumeMessages(workerTask.taskRole)
    eventBus.grantPutEventsTo(workerTask.taskRole)
    key.grantEncryptDecrypt(apiTask.taskRole)
    key.grantEncryptDecrypt(workerTask.taskRole)

    const albSecurityGroup = new ec2.SecurityGroup(this, 'AlbSecurityGroup', {
      vpc,
      description: 'Public HTTP access to the ERP API',
      allowAllOutbound: true,
    })
    albSecurityGroup.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(80), 'HTTP from the internet')

    const apiSecurityGroup = new ec2.SecurityGroup(this, 'ApiSecurityGroup', {
      vpc,
      description: 'ERP API tasks',
      allowAllOutbound: true,
    })
    apiSecurityGroup.addIngressRule(albSecurityGroup, ec2.Port.tcp(8080), 'HTTP from ALB')

    const workerSecurityGroup = new ec2.SecurityGroup(this, 'WorkerSecurityGroup', {
      vpc,
      description: 'ERP worker tasks',
      allowAllOutbound: true,
    })

    const migratorSecurityGroup = new ec2.SecurityGroup(this, 'MigratorSecurityGroup', {
      vpc,
      description: 'One-off database migrations',
      allowAllOutbound: true,
    })
    databaseSecurityGroup.addIngressRule(apiSecurityGroup, ec2.Port.tcp(5432), 'PostgreSQL from API')
    databaseSecurityGroup.addIngressRule(workerSecurityGroup, ec2.Port.tcp(5432), 'PostgreSQL from worker')
    databaseSecurityGroup.addIngressRule(migratorSecurityGroup, ec2.Port.tcp(5432), 'PostgreSQL from migrator')

    const alb = new elbv2.ApplicationLoadBalancer(this, 'Alb', {
      vpc,
      internetFacing: true,
      securityGroup: albSecurityGroup,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
    })

    const apiService = new ecs.FargateService(this, 'ApiService', {
      cluster,
      taskDefinition: apiTask,
      desiredCount: 1,
      assignPublicIp: true,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroups: [apiSecurityGroup],
      circuitBreaker: { rollback: true },
      minHealthyPercent: 0,
      maxHealthyPercent: 100,
    })

    new ecs.FargateService(this, 'WorkerService', {
      cluster,
      taskDefinition: workerTask,
      desiredCount: 1,
      assignPublicIp: true,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroups: [workerSecurityGroup],
      circuitBreaker: { rollback: true },
      minHealthyPercent: 0,
      maxHealthyPercent: 100,
    })

    const listener = alb.addListener('Http', {
      port: 80,
      open: false,
      defaultAction: elbv2.ListenerAction.fixedResponse(404, {
        contentType: 'text/plain',
        messageBody: 'not found',
      }),
    })

    listener.addTargets('ApiTargets', {
      port: 8080,
      protocol: elbv2.ApplicationProtocol.HTTP,
      targets: [apiService],
      healthCheck: {
        path: '/health',
        healthyHttpCodes: '200',
        interval: cdk.Duration.seconds(30),
      },
      deregistrationDelay: cdk.Duration.seconds(10),
    })

    new budgets.CfnBudget(this, 'MonthlyBudget', {
      budget: {
        budgetName: `erp-${environmentName}-monthly`,
        budgetType: 'COST',
        timeUnit: 'MONTHLY',
        budgetLimit: {
          amount: monthlyBudgetUsd,
          unit: 'USD',
        },
        costFilters: {
          TagKeyValue: ['user:Project$cvs-erp', `user:Environment$${environmentName}`],
        },
      },
    })

    new cdk.CfnOutput(this, 'ApiUrl', { value: `http://${alb.loadBalancerDnsName}` })
    new cdk.CfnOutput(this, 'HealthUrl', { value: `http://${alb.loadBalancerDnsName}/health` })
    new cdk.CfnOutput(this, 'OpenApiUrl', { value: `http://${alb.loadBalancerDnsName}/openapi/v1.json` })
    new cdk.CfnOutput(this, 'UserPoolId', { value: userPool.userPoolId })
    new cdk.CfnOutput(this, 'UserPoolClientId', { value: userPoolClient.userPoolClientId })
    new cdk.CfnOutput(this, 'CognitoAuthority', {
      value: `https://cognito-idp.${this.region}.amazonaws.com/${userPool.userPoolId}`,
    })
    new cdk.CfnOutput(this, 'DatabaseEndpoint', { value: database.instanceEndpoint.hostname })
    new cdk.CfnOutput(this, 'DocumentsBucketName', { value: documents.bucketName })
    new cdk.CfnOutput(this, 'WorkerQueueUrl', { value: workerQueue.queueUrl })
    new cdk.CfnOutput(this, 'WorkerDlqUrl', { value: deadLetterQueue.queueUrl })
    new cdk.CfnOutput(this, 'ClusterName', { value: cluster.clusterName })
    new cdk.CfnOutput(this, 'PublicSubnetIds', {
      value: vpc.publicSubnets.map((subnet) => subnet.subnetId).join(','),
    })
    new cdk.CfnOutput(this, 'MigratorTaskDefinitionArn', { value: migratorTask.taskDefinitionArn })
    new cdk.CfnOutput(this, 'MigratorSecurityGroupId', { value: migratorSecurityGroup.securityGroupId })
    new cdk.CfnOutput(this, 'DestroyCommand', {
      value: 'npx cdk destroy Erp-dev --force',
    })
  }
}
