using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.WAFv2;
using Constructs;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using static Amazon.CDK.AWS.WAFv2.CfnWebACL;

//Resolve Lambda/Cloudfront ambiguous reference
using Function = Amazon.CDK.AWS.Lambda.Function;
using FunctionProps = Amazon.CDK.AWS.Lambda.FunctionProps;

namespace IacCdk
{
    public class IacCdkStack : Stack
    {
        internal IacCdkStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            #region "DynamoDB"
            var rankingTable = new TableV2(this, "Rating", new TablePropsV2
            {
                TableName = "Ratings",
                RemovalPolicy = RemovalPolicy.DESTROY,
                PartitionKey = new Attribute
                {
                    Name = "Id",
                    Type = AttributeType.NUMBER
                },
                SortKey = new Attribute
                {
                    Name = "Points",
                    Type = AttributeType.STRING
                },
                Billing = Billing.OnDemand()
            });
            #endregion

            #region "SQS"
            var fifoQueue = new Queue(this, "ProcessingQueue", new QueueProps
            {
                QueueName = "ProcessingQueue",
                Fifo = true
            });

            var processingQueue = new SqsQueue(fifoQueue);
            #endregion

            #region "Lambda"
            var lambdaEnvVariables = new Dictionary<string, string>
            {
                {"TABLE_NAME", rankingTable.TableName},
            };

            var dataCleanerFunction = new Function(this, "DataCleanerFunction", new FunctionProps
            {
                FunctionName = "DataCleanerFunction",
                Runtime = Runtime.PYTHON_3_11,
                Environment = lambdaEnvVariables,
                Timeout = Duration.Seconds(10)
            });

            var dataProcessorFunction = new Function(this, "DataProcessorFunction", new FunctionProps
            {
                FunctionName = "DataProcessorFunction",
                Runtime = Runtime.PYTHON_3_11,
                Environment = lambdaEnvVariables,
            });

            var globalRankingFunction = new Function(this, "GlobalRankingFunction", new FunctionProps
            {
                FunctionName = "GlobalRankingFunction",
                Runtime = Runtime.PYTHON_3_11,
                Environment = lambdaEnvVariables
            });

            var tournamentRankingFunction = new Function(this, "TournamentRankingFunction", new FunctionProps
            {
                FunctionName = "TournamentRankingFunction",
                Runtime = Runtime.PYTHON_3_11,
                Environment = lambdaEnvVariables
            });
            #endregion

            #region "S3"
            var dataLoadBucket = new Bucket(this, "DataLoadBucket", new BucketProps
            {
                BucketName = "DataLoadBucket"
            });

            var staticReactBucket = new Bucket(this, "StaticReactBucket", new BucketProps
            {
                BucketName = "StaticReactBucket",

            });
            #endregion

            #region "API Gateway"
            var apiGateway = new RestApi(this, "PowerRankApi", new RestApiProps
            {
                RestApiName = "PowerRankApi",
                Description = "PowerRank Lambda Powered Backend API",
                DeployOptions = new StageOptions
                {
                    StageName = "Test",
                    ThrottlingBurstLimit = 10,
                    ThrottlingRateLimit = 10,
                    LoggingLevel = MethodLoggingLevel.INFO,
                    MetricsEnabled = true
                },
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = new string[]
                    {
                        "" //add react s3 bucket origin
                    }
                }
            });
            #endregion

            #region "WAF"
            //ref: https://github.com/aws-samples/aws-cdk-examples/blob/master/csharp/CloudFront-S3-WAF/src/CdkStack.cs

            //Get the local machine Ip address.    
            var localIpAddress = GetIpAddressAsync().Result + "/32";

            //Restrict website access based on IP address by creating WAF Web ACL
            CfnIPSet cfnIPSet = new CfnIPSet(
                this,
                "AllowedIPs",
                new CfnIPSetProps
                {
                    Addresses = new string[] { localIpAddress }, //Provide list of allowed IP address. You can provide CIDR address as well.
                    IpAddressVersion = "IPV4",
                    Scope = "CLOUDFRONT"
                }
            );

            CfnWebACL cfnWebACL = new CfnWebACL(
                this,
                "WebACL",
                new CfnWebACLProps
                {
                    DefaultAction = new DefaultActionProperty
                    {
                        Block = new BlockActionProperty
                        {
                            CustomResponse = new CustomResponseProperty { ResponseCode = 403 }
                        }
                    },
                    Scope = "CLOUDFRONT",
                    VisibilityConfig = new VisibilityConfigProperty
                    {
                        CloudWatchMetricsEnabled = true,
                        MetricName = "WebACLMetric",
                        SampledRequestsEnabled = true
                    },
                    Rules = new[]
                    {
                        new RuleProperty
                        {
                            Name = "WebACLRule",
                            Priority = 1,
                            Statement = new StatementProperty
                            {
                                IpSetReferenceStatement = new IPSetReferenceStatementProperty
                                {
                                    Arn = cfnIPSet.AttrArn
                                }
                            },
                            VisibilityConfig = new VisibilityConfigProperty
                            {
                                CloudWatchMetricsEnabled = true,
                                MetricName = "WebACLRuleMetric",
                                SampledRequestsEnabled = true
                            },
                            Action = new RuleActionProperty { Allow = new AllowActionProperty() }
                        }
                    }
                }
            );
            #endregion

            #region "Cloudfront"
            var cloudFrontDistribution = new Distribution(this, "CloudFrontDistribution", new DistributionProps
            {
                DefaultBehavior = new BehaviorOptions
                {
                    Origin = new S3Origin(staticReactBucket)
                },
                WebAclId = cfnWebACL.AttrArn,
                PriceClass = PriceClass.PRICE_CLASS_100
            });
            #endregion

            #region "EventMappings"
            // Add SQS event source to data processor
            dataProcessorFunction.AddEventSource(new SqsEventSource(fifoQueue, new SqsEventSourceProps
            {
                MaxConcurrency = 1
            }));

            dataCleanerFunction.AddEventSource(new S3EventSource(dataLoadBucket, new S3EventSourceProps
            {
                Events = new[]
                {
                    EventType.OBJECT_CREATED,
                }
            }));
            #endregion

            #region "Permissions"
            rankingTable.GrantReadWriteData(dataProcessorFunction);
            rankingTable.GrantReadData(globalRankingFunction);
            rankingTable.GrantReadData(tournamentRankingFunction);

            dataLoadBucket.GrantRead(dataCleanerFunction);
            #endregion

            #region "Output"
            new CfnOutput(this, "CloudFront URL", new CfnOutputProps
            {
                Value = string.Format(
                    "https://{0}/index.html",
                    cloudFrontDistribution.DomainName
                    )
            });
            #endregion
        }

        private async Task<string> GetIpAddressAsync()
        {
            HttpClient client = new HttpClient();
            string ipAddress = await client
                .GetStringAsync("http://checkip.amazonaws.com/")
                .ConfigureAwait(continueOnCapturedContext: false);
            return ipAddress.Replace("\n", "");
        }
    }
}
