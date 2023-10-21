using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.ECR;
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

namespace IacCdk
{
    public class IacCdkStack : Stack
    {
        internal IacCdkStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            #region "DynamoDB"
            var pointsLocalSecondaryIndex = new LocalSecondaryIndexProps
            {
                IndexName = "PointsLSI",
                SortKey = new Attribute
                {
                    Name = "Points",
                    Type = AttributeType.NUMBER
                }
            };

            var rankingTable = new TableV2(this, "Rating", new TablePropsV2
            {
                TableName = "Ratings",
                RemovalPolicy = RemovalPolicy.DESTROY,
                PartitionKey = new Attribute
                {
                    Name = "PK",
                    Type = AttributeType.STRING
                },
                SortKey = new Attribute
                {
                    Name = "SK",
                    Type = AttributeType.STRING
                },
                LocalSecondaryIndexes = new LocalSecondaryIndexProps[1]{
                    pointsLocalSecondaryIndex
                },
                Billing = Billing.OnDemand()
            });
            #endregion

            #region "ECR"
            var dataCleanECR = new Repository(this, "data-cleaner", new RepositoryProps
            {
                RepositoryName = "data-cleaner",
                AutoDeleteImages = true
            });

            var dataProcessECR = new Repository(this, "data-processor", new RepositoryProps
            {
                RepositoryName = "data-cleaner",
                AutoDeleteImages = true
            });

            var globalRankECR = new Repository(this, "get-global-rank", new RepositoryProps
            {
                RepositoryName = "get-global-rank",
                AutoDeleteImages = true
            });

            var tournamentRankECR = new Repository(this, "get-tournament-rank", new RepositoryProps
            {
                RepositoryName = "get-global-rank",
                AutoDeleteImages = true
            });

            var tournamentECR = new Repository(this, "get-tournaments", new RepositoryProps
            {
                RepositoryName = "get-global-rank",
                AutoDeleteImages = true
            });

            var teamECR = new Repository(this, "get-teams", new RepositoryProps
            {
                RepositoryName = "get-global-rank",
                AutoDeleteImages = true
            });
            #endregion

            #region "Lambda"
            var lambdaEnvVariables = new Dictionary<string, string>
            {
                {"TABLE_NAME", rankingTable.TableName},
                {"POINTS_LSI_NAME", pointsLocalSecondaryIndex.IndexName},
            };

            var baseImage = DockerImageCode.FromImageAsset("../Base.Python39.Lambda");

            var globalRankingFunction = new DockerImageFunction(this, "GlobalRankingFunction", new DockerImageFunctionProps
            {
                Code = baseImage,
                Environment = lambdaEnvVariables
            });

            var tournamentRankingFunction = new DockerImageFunction(this, "TournamentRankingFunction", new DockerImageFunctionProps
            {
                Code = baseImage,
                Environment = lambdaEnvVariables
            });

            var tournamentsFunction = new DockerImageFunction(this, "TournamentsFunction", new DockerImageFunctionProps
            {
                Code = baseImage,
                Environment = lambdaEnvVariables
            });

            var teamsFunction = new DockerImageFunction(this, "TeamsFunction", new DockerImageFunctionProps
            {
                Code = baseImage,
                Environment = lambdaEnvVariables
            });
            #endregion

            #region "S3"
            var staticReactBucket = new Bucket(this, "StaticReactBucket");
            #endregion

            #region "API Gateway"
            var apiGateway = new RestApi(this, "PowerRankApi", new RestApiProps
            {
                RestApiName = "PowerRankApi",
                Description = "PowerRank Lambda Powered Backend API",
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = new string[]
                    {
                        staticReactBucket.BucketWebsiteUrl
                    }
                }
            });

            var test2 = new SpecRestApi(this, "", new SpecRestApiProps
            {
                RestApiName = "",
                ApiDefinition = ApiDefinition.FromAsset("")
            });

            var test = new AssetApiDefinition("");


            #endregion

            //#region "WAF"
            ////ref: https://github.com/aws-samples/aws-cdk-examples/blob/master/csharp/CloudFront-S3-WAF/src/CdkStack.cs

            ////Get the local machine Ip address.    
            //var localIpAddress = GetIpAddressAsync().Result + "/32";

            ////Restrict website access based on IP address by creating WAF Web ACL
            //CfnIPSet cfnIPSet = new CfnIPSet(
            //    this,
            //    "AllowedIPs",
            //    new CfnIPSetProps
            //    {
            //        Addresses = new string[] { localIpAddress }, //Provide list of allowed IP address. You can provide CIDR address as well.
            //        IpAddressVersion = "IPV4",
            //        Scope = "CLOUDFRONT"
            //    }
            //);

            //CfnWebACL cfnWebACL = new CfnWebACL(
            //    this,
            //    "WebACL",
            //    new CfnWebACLProps
            //    {
            //        DefaultAction = new DefaultActionProperty
            //        {
            //            Block = new BlockActionProperty
            //            {
            //                CustomResponse = new CustomResponseProperty { ResponseCode = 403 }
            //            }
            //        },
            //        Scope = "CLOUDFRONT",
            //        VisibilityConfig = new VisibilityConfigProperty
            //        {
            //            CloudWatchMetricsEnabled = true,
            //            MetricName = "WebACLMetric",
            //            SampledRequestsEnabled = true
            //        },
            //        Rules = new[]
            //        {
            //            new RuleProperty
            //            {
            //                Name = "WebACLRule",
            //                Priority = 1,
            //                Statement = new StatementProperty
            //                {
            //                    IpSetReferenceStatement = new IPSetReferenceStatementProperty
            //                    {
            //                        Arn = cfnIPSet.AttrArn
            //                    }
            //                },
            //                VisibilityConfig = new VisibilityConfigProperty
            //                {
            //                    CloudWatchMetricsEnabled = true,
            //                    MetricName = "WebACLRuleMetric",
            //                    SampledRequestsEnabled = true
            //                },
            //                Action = new RuleActionProperty { Allow = new AllowActionProperty() }
            //            }
            //        }
            //    }
            //);
            //#endregion

            //#region "Cloudfront"
            //var cloudFrontDistribution = new Distribution(this, "CloudFrontDistribution", new DistributionProps
            //{
            //    DefaultBehavior = new BehaviorOptions
            //    {
            //        Origin = new S3Origin(staticReactBucket)
            //    },
            //    WebAclId = cfnWebACL.AttrArn,
            //    PriceClass = PriceClass.PRICE_CLASS_100
            //});
            //#endregion

            #region "Permissions"
            rankingTable.GrantReadData(globalRankingFunction);
            rankingTable.GrantReadData(tournamentRankingFunction);
            rankingTable.GrantReadData(tournamentsFunction);
            rankingTable.GrantReadData(teamsFunction); 
            #endregion

            #region "Output"
            //new CfnOutput(this, "CloudFront URL", new CfnOutputProps
            //{
            //    Value = string.Format(
            //        "https://{0}/index.html",
            //        cloudFrontDistribution.DomainName
            //        )
            //});

            new CfnOutput(this, "APi Gateway URL", new CfnOutputProps
            {
                Value = apiGateway.Url
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
