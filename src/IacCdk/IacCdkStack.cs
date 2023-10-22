using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Constructs;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace IacCdk
{
    public class IacCdkStack : Stack
    {
        const string SITEURL = "duzlyw7tx07x8.cloudfront.net/";
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
            var globalRankECR = new Repository(this, "get-global-rank", new RepositoryProps
            {
                RepositoryName = "get-global-rank",
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteImages = true
            });

            var tournamentRankECR = new Repository(this, "get-tournament-rank", new RepositoryProps
            {
                RepositoryName = "get-tournament-rank",
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteImages = true
            });

            var tournamentECR = new Repository(this, "get-tournaments", new RepositoryProps
            {
                RepositoryName = "get-tournaments",
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteImages = true
            });

            var teamECR = new Repository(this, "get-teams", new RepositoryProps
            {
                RepositoryName = "get-teams",
                RemovalPolicy = RemovalPolicy.DESTROY,
                AutoDeleteImages = true
            });
            #endregion

            #region "Lambda"
            var lambdaEnvVariables = new Dictionary<string, string>
            {
                {"TABLE_NAME", rankingTable.TableName},
                {"POINTS_LSI_NAME", pointsLocalSecondaryIndex.IndexName},
            };

            var baseImage = DockerImageCode.FromImageAsset("./src/Base.Python39.Lambda");

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

            #region "API Gateway"
            var apiGateway = new RestApi(this, "PowerRankApi", new RestApiProps
            {
                RestApiName = "PowerRankApi",
                Description = "PowerRank Serverless Backend API",
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = new string[]
                    {
                        SITEURL
                    }
                }
            });

            var globalRankings = apiGateway.Root.AddResource("global_rankings");
            globalRankings.AddMethod("GET", new LambdaIntegration(globalRankingFunction));

            var tournamentRankings = apiGateway.Root.AddResource("tournament_rankings").AddResource("{tournament_id}");
            tournamentRankings.AddMethod("GET", new LambdaIntegration(tournamentRankingFunction));

            var teamRankings = apiGateway.Root.AddResource("team_rankings");
            teamRankings.AddMethod("GET", new LambdaIntegration(globalRankingFunction));

            var tournaments = apiGateway.Root.AddResource("tournaments");
            tournaments.AddMethod("GET", new LambdaIntegration(tournamentsFunction));

            var teams = apiGateway.Root.AddResource("teams");
            teams.AddMethod("GET", new LambdaIntegration(teamsFunction));

            #endregion

            #region "Permissions"
            rankingTable.GrantReadData(globalRankingFunction);
            rankingTable.GrantReadData(tournamentRankingFunction);
            rankingTable.GrantReadData(tournamentsFunction);
            rankingTable.GrantReadData(teamsFunction); 
            #endregion

            #region "Output"
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
