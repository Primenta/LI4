using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;

namespace BetterFindsProject.Pages
{
    // Indica-nos que apenas utilizadores autenticados podem aceder a esta p�gina
    [Authorize]
    public class LeiloesModel : PageModel
    {
        private readonly string _connectionString;

        // Usamos este construtor para obter a string conection que permite a conex�o com a base de dados
        public LeiloesModel(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Lida com os dados enviados pelo formul�rio HTLM ([BindProperty]serve para fazer essa liga��o autom�ticamente) 
        public class AuctionItem
        {
            public int AuctionId { get; set; }
            public string Name { get; set; }
            public int Price { get; set; }
            public int AuctionTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Description { get; set; }
            public int MinimumBid { get; set; }
            public int ClientId { get; set; }
            public bool IsAuctionEnded { get; set; }
            public bool IsWinner { get; set; }
            public bool IsCreator { get; set; }

        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public int Licitacao { get; set; }
        }

        public List<AuctionItem> Auctions { get; set; } = new List<AuctionItem>();

        public async Task OnGetAsync()
        {
            // Verifica se o utilizador est� autenticado
            if (User.Identity.IsAuthenticated)
            {
                // Vai buscar os leil�es � base de dados
                Auctions = await GetAuctionsFromDatabaseAsync();

                // Itera sobre os leil�es 
                foreach (var auction in Auctions)
                {
                    // Verifica se os leil�es terminararem comprarando o tempo atual com o tempo de fim do leil�o
                    auction.IsAuctionEnded = DateTime.Now > auction.EndTime;

                    // Verifica se o cliente logado � o criador do leil�o
                    auction.IsCreator = await IsCreator(auction.AuctionId);

                    if (auction.IsAuctionEnded)
                    {
                        // Verifica se o cliente logado � o vencedor do leil�o se o leil�o terminou
                        auction.IsWinner = await IsWinner(auction.AuctionId);
                    }
                }
            }
            else
            {
                // O utilizador n�o est� autenticado e redireciona para a p�gina de login
                RedirectToPage("/Login");
            }
        }

        // Esta fun��o vai buscar todos os leil�es � base de dados
        private async Task<List<AuctionItem>> GetAuctionsFromDatabaseAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var auctionItems = await connection.QueryAsync<AuctionItem>("SELECT * FROM Auction");
                return auctionItems.ToList();
            }
        }

        private async Task<bool> IsWinner(int auctionId)
        {
            // Vai buscar os Id�s do vencedor do leil�o e do cliente logado
            int? highestBidderClientId = await GetHighestBidderClientId(auctionId);
            int? loggedInClientId = await GetLoggedInClientId();

            // Verifica se o vencedor do leil�o � o cliente que est� logado
            return highestBidderClientId.HasValue && highestBidderClientId.Value == loggedInClientId;
        }

        private async Task<bool> IsCreator(int auctionId)
        {
            // Vai buscar os Id�s do criador do leil�o e do cliente logado
            int? auctionCreatorClientId = await GetAuctionCreatorClientId(auctionId);
            int? loggedInClientId = await GetLoggedInClientId();

            // Verifica se o criador do leil�o � o cliente que est� logado
            return auctionCreatorClientId.HasValue && auctionCreatorClientId.Value == loggedInClientId;
        }

        // Consulta para obter o ClientId do maior licitante do leil�o 
        private async Task<int?> GetHighestBidderClientId(int auctionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string query = "SELECT TOP 1 ClientId FROM Bid WHERE AuctionId = @AuctionId ORDER BY Value DESC";

                return await connection.ExecuteScalarAsync<int?>(query, new { AuctionId = auctionId });
            }
        }

        // Consulta para obter o ClientId do criador do leil�o
        private async Task<int?> GetAuctionCreatorClientId(int auctionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string query = "SELECT ClientId FROM Auction WHERE AuctionId = @AuctionId";

                return await connection.ExecuteScalarAsync<int?>(query, new { AuctionId = auctionId });
            }
        }

        // Consulta para obter o ClientId do cliente logado
        private async Task<int?> GetLoggedInClientId()
        {
            if (User.Identity.IsAuthenticated)
            {
                string userEmail = User.Identity.Name;
                string queryGetClientId = "SELECT ClientId FROM Client WHERE Email = @UserEmail";

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    // Abre uma conex�o ass�ncrina com a base de dados
                    await con.OpenAsync();

                    // Retorna o ID do cliente obtido pela consulta SQL. 
                    return await con.ExecuteScalarAsync<int?>(queryGetClientId, new { UserEmail = userEmail });
                }
            }

            return null;
        }

        /* Processa os dados do formul�rio enviado pelo usutilizador, valida os dados, e atualiza a licita��o do leil�o 
         * na base de dados, se todas as valida��es forem realizadas com sucesso */
        public async Task<IActionResult> OnPostAsync(int auctionId)
        {
            try
            {
                // Verifica se o usu�rio est� autenticado
                if (!User.Identity.IsAuthenticated)
                {
                    return RedirectToPage("/Login");
                }

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();

                    var auctionInfo = await con.QueryFirstOrDefaultAsync<AuctionItem>("SELECT * FROM Auction WHERE AuctionId = @AuctionId", new { AuctionId = auctionId });

                    // N�o permite fazer licita��o inferior � MinimumBid
                    if (Input.Licitacao < auctionInfo.MinimumBid)
                    {
                        ModelState.AddModelError(string.Empty, "A sua licita��o � inferior � licita��o m�nima estabelecida pelo vendedor.");
                        return RedirectToPage("/Leiloes");
                    }

                    // N�o permite fazer licita��o inferior ao pre�o atual
                    if (Input.Licitacao < auctionInfo.Price)
                    {
                        ModelState.AddModelError(string.Empty, "No momento existe uma licita��o maior");
                        return RedirectToPage("/Leiloes");
                    }

                    // Atualiza o pre�o na tabela de leil�es
                    string queryUpdateAuction = "UPDATE Auction SET Licitacao = @Licitacao, Price = @Licitacao WHERE AuctionId = @AuctionId";
                    using (SqlCommand cmdUpdateAuction = new SqlCommand(queryUpdateAuction, con))
                    {
                        cmdUpdateAuction.Parameters.AddWithValue("@Licitacao", Input.Licitacao);
                        cmdUpdateAuction.Parameters.AddWithValue("@AuctionId", auctionId);

                        int rowsAffected = await cmdUpdateAuction.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return RedirectToPage("/Leiloes");
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, "Failed to update auction. No rows affected.");
                            return Page();
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                ModelState.AddModelError(string.Empty, $"SQL Error: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
            }
            return Page();
        }

    }
}