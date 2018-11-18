﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AdvantagePlatform.Data;
using IdentityModel.Client;
using IdentityModel.Jwk;
using IdentityServer4.EntityFramework.Entities;
using IdentityServer4.EntityFramework.Interfaces;
using LtiAdvantage.IdentityServer4;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AdvantagePlatform.Pages.Tools
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfigurationDbContext _identityContext;

        public EditModel(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfigurationDbContext identityContext)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _identityContext = identityContext;
        }

        [BindProperty]
        public ToolModel Tool { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var tool = user.Tools.SingleOrDefault(t => t.Id == id);
            if (tool == null)
            {
                return NotFound();
            }

            var client = await _identityContext.Clients
                .Include(c => c.ClientSecrets)
                .SingleOrDefaultAsync(c => c.Id == tool.IdentityServerClientId);
            if (client == null)
            {
                return NotFound();
            }

            Tool = new ToolModel(tool, client);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (Tool.JsonWebKeySetUrl.IsPresent())
            {
                var httpClient = _httpClientFactory.CreateClient();

                // Test whether JsonWebKeySetUrl points to a Discovery Document
                var disco = await httpClient.GetDiscoveryDocumentAsync(Tool.JsonWebKeySetUrl);
                if (!disco.IsError)
                {
                    Tool.JsonWebKeySetUrl = disco.JwksUri;
                }
                else
                {
                    // Test that JsonWebKeySetUrl points to a JWKS endpoint
                    try
                    {
                        var keySetJson = await httpClient.GetStringAsync(Tool.JsonWebKeySetUrl);
                        JsonConvert.DeserializeObject<JsonWebKeySet>(keySetJson);
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError($"{nameof(Tool)}.{nameof(Tool.JsonWebKeySetUrl)}",
                            e.Message);
                        return Page();
                    }
                }
            }

            if (Tool.JsonWebKeySetUrl.IsMissing() && Tool.PublicKey.IsMissing())
            {
                ModelState.AddModelError($"{nameof(Tool)}.{nameof(Tool.JsonWebKeySetUrl)}",
                    "Either JSON Web Key Set URL or Public Key is required.");
                ModelState.AddModelError($"{nameof(Tool)}.{nameof(Tool.PublicKey)}",
                    "Either JSON Web Key Set URL or Public Key is required.");
                return Page();
            }

            var tool = await _context.Tools.FindAsync(Tool.Id);
            tool.Name = Tool.Name;
            tool.JsonWebKeySetUrl = Tool.JsonWebKeySetUrl;
            tool.LaunchUrl = Tool.LaunchUrl;

            _context.Tools.Attach(tool).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var client = await _identityContext.Clients
                .Include(c => c.ClientSecrets)
                .SingleOrDefaultAsync(c => c.Id == tool.IdentityServerClientId);

            var publicKey = client.ClientSecrets
                .SingleOrDefault(s => s.Type == Constants.SecretTypes.PublicPemKey);

            if (Tool.PublicKey.IsPresent())
            {
                if (publicKey == null)
                {
                    publicKey = new ClientSecret
                    {
                        Client = client,
                        Type = Constants.SecretTypes.PublicPemKey
                    };
                    client.ClientSecrets.Add(publicKey);
                }
                publicKey.Value = Tool.PublicKey;
            }
            else
            {
                if (publicKey != null)
                {
                    client.ClientSecrets.Remove(publicKey);
                }
            }

            _identityContext.Clients.Update(client);
            await _identityContext.SaveChangesAsync();


            return RedirectToPage("./Index");
        }
    }
}
