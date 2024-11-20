using BotSharp.Abstraction.Agents.Settings;
using BotSharp.Abstraction.Conversations;
using BotSharp.Abstraction.Functions.Models;
using BotSharp.Abstraction.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace BotSharp.Abstraction.Agents;

public abstract class AgentHookBase : IAgentHook
{
    public virtual string SelfId => throw new NotImplementedException("Please set SelfId as agent id!");

    protected Agent _agent;
    public Agent Agent => _agent;
    
    protected readonly IServiceProvider _services;
    protected readonly AgentSettings _settings;

    public AgentHookBase(IServiceProvider services, AgentSettings settings)
    {
        _services = services;
        _settings = settings;
    }

    public void SetAget(Agent agent)
    {
        _agent = agent;
    }

    public virtual bool OnAgentLoading(ref string id)
    {
        return true;
    }

    public virtual bool OnInstructionLoaded(string template, Dictionary<string, object> dict)
    {
        dict["current_date"] = $"{DateTime.Now:MMM dd, yyyy}";
        dict["current_time"] = $"{DateTime.Now:hh:mm tt}";
        dict["current_weekday"] = $"{DateTime.Now:dddd}";
        return true;
    }

    public virtual bool OnFunctionsLoaded(List<FunctionDef> functions)
    {
        _agent.Functions = functions;
        return true;
    }

    public virtual bool OnSamplesLoaded(List<string> samples)
    {
        _agent.Samples = samples;
        return true;
    }

    public virtual void  OnAgentLoaded(Agent agent)
    {
    }

    public virtual void OnLoadAgentUtility(Agent agent, IEnumerable<AgentUtilityLoadModel> utilities)
    {
        if (agent.Type == AgentType.Routing || utilities.IsNullOrEmpty()) return;

        var conv = _services.GetRequiredService<IConversationService>();
        var isConvMode = conv.IsConversationMode();
        if (!isConvMode) return;

        var render = _services.GetRequiredService<ITemplateRender>();

        agent.Functions ??= [];
        var agentUtilities = agent.Utilities ?? [];

        foreach (var item in utilities)
        {
            if (item.UtilityName.IsNullOrEmpty() || item.Content == null) continue;

            var isEnabled = agentUtilities.Contains(item.UtilityName);
            if (!isEnabled) continue;

            var (fns, prompts) = GetUtilityContent(item.Content);

            if (!fns.IsNullOrEmpty())
            {
                agent.Functions.AddRange(fns);
            }

            if (!prompts.IsNullOrEmpty())
            {
                foreach (var prompt in prompts)
                {
                    agent.Instruction += $"\r\n\r\n{prompt}\r\n\r\n";
                }
            }
        }
    }

    private (IEnumerable<FunctionDef>, IEnumerable<string>) GetUtilityContent(UtilityContent content)
    {
        var db = _services.GetRequiredService<IBotSharpRepository>();
        var render = _services.GetRequiredService<ITemplateRender>();

        var fns = new List<FunctionDef>();
        var prompts = new List<string>();

        var agent = db.GetAgent(BuiltInAgentId.UtilityAssistant);
        if (agent == null)
        {
            return (fns, prompts);
        }
        
        if (!content.Functions.IsNullOrEmpty())
        {
            var functionNames = content.Functions?.Select(x => x.Name)?.ToList() ?? [];
            fns = agent?.Functions?.Where(x => functionNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase))?.ToList() ?? [];
        }

        if (!content.Templates.IsNullOrEmpty())
        {
            foreach (var template in content.Templates)
            {
                var prompt = agent?.Templates?.FirstOrDefault(x => x.Name.IsEqualTo(template.Name))?.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prompt)) continue;

                if (!template.Data.IsNullOrEmpty())
                {
                    prompt = render.Render(prompt, template.Data);
                }
                prompts.Add(prompt);
            }
        }

        return (fns, prompts);
    }
}
