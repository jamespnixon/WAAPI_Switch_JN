﻿using System;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace WAAPI_Switch_JN
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            _Main().Wait();
        }

        static async Task _Main()
        {
            var client = CreateConnection().Result;
            var switches = GetSwitchObjects(client).Result;

            await AssignSwitchContainers(client, switches);
            await GetSwitchAssignments(client, switches);
            await SetSwitchAssignments(client, switches);
            await client.Close();

            client = CreateConnection().Result;
            await SetDefaults(client, switches);
            await client.Close();

        }

        private static async Task SetDefaults(AK.Wwise.Waapi.JsonClient client, SwitchCollection switches)
        {
        
        Start:

            Console.WriteLine("Would you like to set default assignments? (y/n)");
          
            var result = Console.ReadKey();
            Console.WriteLine();

            if (result.KeyChar == 'y')
            {

                // create a new switch collection list to house containers that don't have default switch assignments
                List<SwitchGroup> defaultSwitchesUnassigned = new List<SwitchGroup>();

                //query the default switch or state value for each switch container
                foreach (var container in switches.containers)
                {
                    var resultDefaultCheck = await client.Call(
                    ak.wwise.core.@object.get,
                    new JObject
                    (
                        new JProperty("from", new JObject(new JProperty("id", new JArray(new string[] { container.id }))))
                    ),
                    new JObject
                    (
                        new JProperty("return", new string[] { "@DefaultSwitchOrState" })
                    ));

                    // create a token for the data returned from DefaultSwitchOrState
                    var token = resultDefaultCheck["return"][0]["@DefaultSwitchOrState"];                 

                    // if the name of the token equals null, add it to a list of switch containers that need assignment
                    if (token["name"] == null)
                        {
                        
                        Console.WriteLine("Adding " + container.id + " to defaultSwitchUnassigned list");
                        defaultSwitchesUnassigned.Add(token.ToObject<SwitchGroup>());
                                             
                    }
           
                }
                //*************************************************************
                // NEED TO FIGURE OUT HOW TO LIMIT THE NEXT STEP TO THE CONTAINERS FOUND IN defautlSwitchesUnassigned  
                //*************************************************************

                foreach (var group in switches.groups)
                {
                    int choice = 1;
                    Console.WriteLine("Select default assignment for " + group.name + " : ");

                    foreach (var child in group.switches)
                    {
                        Console.WriteLine(choice + " - " + child.name);
                        choice++;
                    }

                Select:

                    Console.WriteLine("Enter Selection: ");
                    var index = Console.ReadKey();
                    Console.WriteLine();

                    if (int.Parse(index.KeyChar.ToString()) > group.switches.Count || int.Parse(index.KeyChar.ToString()) < 1 || !(char.IsDigit(index.KeyChar)))
                    {
                        goto Select;
                    }
                    else
                    {
                        var container = switches.containers.Where(c => c.name == group.name).First();
                        var defaultSwitch = group.switches.ElementAt(int.Parse(index.KeyChar.ToString()) - 1);

                        await client.Call(
                            ak.wwise.core.@object.setReference,
                            new JObject
                            (
                                new JProperty("reference", "DefaultSwitchOrState"),
                                new JProperty("object", container.id),
                                new JProperty("value", defaultSwitch.id)
                            ),
                            null
                        );
                    }
                }
            }
            else if (result.KeyChar == 'n')
            {
                return;
            }
            else
            {
                goto Start;
            }
        }

        private static async Task MatchAndAssignSwitches(AK.Wwise.Waapi.JsonClient client, List<SwitchContainerChild> children, SwitchGroup matchingGroup)
        {
            foreach (var item in children)
            {
                var matchingSwitch = matchingGroup.switches.Where(switchGroup => switchGroup.name == item.name).First();

                await client.Call(
                    ak.wwise.core.switchContainer.addAssignment,
                    new JObject
                    (
                        new JProperty("stateOrSwitch", matchingSwitch.id),
                        new JProperty("child", item.id)
                    )
                    );
            }
        }

        private static async Task CheckAssignments(AK.Wwise.Waapi.JsonClient client, List<SwitchContainerChild> assigned, List<SwitchContainerChild> unassigned, SwitchGroup matchingGroup, SwitchContainer container)
        {
            List<SwitchAssignment> toRemove = new List<SwitchAssignment>();

            foreach (var child in assigned)
            {
                var matchingSwitch = matchingGroup.switches.Where(s => s.name == child.name).First();

                var assignment = container.assignments.Where(a => a.child == child.id).First();

                if (matchingSwitch.id != assignment.stateOrSwitch)
                {
                    toRemove.Add(assignment);

                    unassigned.Add(child);

                    await client.Call
                        (
                        ak.wwise.core.switchContainer.removeAssignment, new JObject
                        (
                            new JProperty("stateOrSwitch", assignment.stateOrSwitch),
                            new JProperty("child", assignment.child)
                            ),
                        null
                        );
                }
            }

            container.assignments = container.assignments.Except(toRemove).ToList();
        }

       

        private static async Task SetSwitchAssignments(AK.Wwise.Waapi.JsonClient client, SwitchCollection switches)
        {
            foreach (var container in switches.containers)
            {
                var unassigned = container.children.Where(c => !container.assignments.Exists(a => a.child == c.id)).ToList();

                var assigned = container.children.Except(unassigned).ToList();

                // Does this assign the first eligble match so it doesn't continue looping through looking for matches???
                var matchingGroup = switches.groups.Where(g => g.name == container.name).First();

                if (assigned.Count > 0)
                {
                    await CheckAssignments(client, assigned, unassigned, matchingGroup, container);
                }

                if (unassigned.Count > 0)
                {
                    await MatchAndAssignSwitches(client, unassigned, matchingGroup);
                }
            }
        }
        private static async Task GetSwitchAssignments(AK.Wwise.Waapi.JsonClient client, SwitchCollection switches)
        {
            foreach (var container in switches.containers)
            {
                var result = await client.Call(
                    ak.wwise.core.switchContainer.getAssignments,
                    new JObject
                    (
                        new JProperty("id", container.id)
                    ),
                    null
            );

            if (result["return"].Count() > 0)
                {
                    var tokens = result["return"];
                    foreach(var assignment in tokens)
                    {
                        Console.WriteLine("adding assignment " + assignment["child"]);
                        container.assignments.Add(assignment.ToObject<SwitchAssignment>());
                    }
                }
            }

        }

        private static async Task AssignSwitchContainers(AK.Wwise.Waapi.JsonClient client, SwitchCollection switches)
        { 
            foreach (var container in switches.containers)
            {
                var result = await client.Call(
                    ak.wwise.core.@object.get,
                    new JObject
                    (
                        new JProperty("from", new JObject(new JProperty("id", new JArray(new string[] { container.id }))))
                    ),
                    new JObject
                    (
                        new JProperty("return", new string[] { "@SwitchGroupOrStateGroup" })
                    ));

                var token = result["return"][0]["@SwitchGroupOrStateGroup"];

                if(token["id"].ToString() == "{00000000-0000-0000-0000-000000000000}"
                    ||token["id"].ToString()  !=switches.groups.Find(group => group.name == container.name).name)
                    { 
                    foreach (var group in switches.groups)
                    {
                        if(container.name == group.name)
                        {
                            await client.Call(
                                ak.wwise.core.@object.setReference,
                                new JObject
                                (
                                    new JProperty("reference", "SwitchGroupOrStateGroup"),
                                    new JProperty("object", container.id),
                                    new JProperty("value", group.id)
                                ),
                                null
                                );
                        }
                    }
                    }
            }
        }

        private static async Task<SwitchCollection> GetSwitchObjects(AK.Wwise.Waapi.JsonClient client)
        {
            SwitchCollection switchCollection = new SwitchCollection();

            var query = new JObject
                (
                 new JProperty("from", new JObject
                 (
                     new JProperty("ofType", new JArray(new string[]
                     { "SwitchContainer", "SwitchGroup"}))
                 ))
                 );

            var options = new JObject
                (
                    new JProperty("return", new string[] { "name", "id", "type" })
                );
            try
            {
                var results = await client.Call(
                    ak.wwise.core.@object.get,
                    query,
                    options);

                Console.WriteLine(results);
                Console.WriteLine();

                var tokens = results["return"];

                List<SwitchContainer> containers = new List<SwitchContainer>();
                List<SwitchGroup> groups = new List<SwitchGroup>();

                foreach (var token in tokens)
                {
                    if (token["type"].ToString() == "SwitchContainer")
                    {
                        
                        var container = token.ToObject<SwitchContainer>();

                        var containerResults = await client.Call(
                            ak.wwise.core.@object.get,
                            new JObject
                            (
                                new JProperty("from", new JObject
                                (
                                    new JProperty("id", new JArray(new string[] { container.id }))
                                    )),
                                    new JProperty("transform", new JArray(new JObject
                                    (
                                        new JProperty("select", new JArray(new string[] { "children" }))
                                )))
                            ),
                            new JObject
                            (
                                new JProperty("return", new string[] { "name", "id" })

                            )

                            );

                        foreach (var switchToken in containerResults["return"])
                        {
                            Console.WriteLine("Adding child " + switchToken["name"] + " to " + container.name);
                            container.children.Add(switchToken.ToObject<SwitchContainerChild>());
                        }

                        containers.Add(container);
                   
                    }
                    if (token["type"].ToString() == "SwitchGroup")
                    {
                        var group = token.ToObject<SwitchGroup>();

                        var groupResults = await client.Call(
                            ak.wwise.core.@object.get,
                            new JObject
                            (
                                new JProperty("from", new JObject
                                (
                                    new JProperty("id", new JArray(new string[] { group.id }))
                                )),
                                new JProperty("transform", new JArray(new JObject
                                (
                                    new JProperty("select", new JArray(new string[] { "children" }))
                                    )))
                                ),
                                new JObject
                                (
                                    new JProperty("return", new string[] {"name", "id"})
                                )

                            );

                        foreach (var switchToken in groupResults["return"])
                        {
                            Console.WriteLine("Adding switch " + switchToken["name"] + " to " + group.name);
                            group.switches.Add(switchToken.ToObject<WwiseSwitch>());
                        }

                        groups.Add(group);

                    }
                }

                groups = groups.Where(group => containers.Any(container => container.name == group.name)).ToList();

                switchCollection.containers = containers;
                switchCollection.groups = groups;
            }
            catch (AK.Wwise.Waapi.Wamp.ErrorException e)
            {
                Console.WriteLine(e.Message);
            }

            return switchCollection;
        }

        private static async Task<AK.Wwise.Waapi.JsonClient> CreateConnection()
        {
            AK.Wwise.Waapi.JsonClient client = new AK.Wwise.Waapi.JsonClient();
            
            await client.Connect();

            client.Disconnected += () =>
            {
                System.Console.WriteLine("We lost connection!");
            };

            return client;
        }

    }
}
