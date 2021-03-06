﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLTKSharp;

namespace HiSum
{
    public class FullStory
    {
        public int id { get; set; }
        public DateTime created_at { get; set; }
        public string author { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string text { get; set; }
        public int points { get; set; }
        [JsonIgnore]
        public int? parent_id { get; set; }
        public DateTime ArchivedOn { get; set; }
        public List<children> children { get; set; }
        string[] stopWords = { "he", "his", "which", "want", "do", "would", "more", "like", "you", "your", "very", "me", "get", "has", "i", "over", "could", "have", "what", "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is", "it", "no", "not", "of", "on", "or", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was", "will", "with" };

        private List<children> NodeList; 

        public Dictionary<string, HashSet<int>> WordIDMapping
        {
            get { return GetWordIDMapping(); }
        }

        Dictionary<string, List<CommentObj>> UserComments { get; set; } 

        public int TotalComments
        {
            get { 
                int count = GetStoryCommentCount();
                return count;
            }
        }

        public int TotalWords
        {
            get
            {
                int count = 0;
                foreach (children child in this.children)
                {
                    string[] allWords = GetAllWords(child.SubtreeText);
                    count += allWords.Length;
                }
                return count;
            }
        }

        /*
         * This method sentence-tokenizes all top level comments
         * The best sentences are those where the words in the sentence
         * occur in the most number of subtree items within the current
         * top level comment
         */
        public List<SentenceObj> GetTopSentences(int N)
        {
            List<SentenceObj> topSentenceObjs = new List<SentenceObj>();
            List<string> topSentences = new List<string>();
            Dictionary<string,double> sentenceScores = new Dictionary<string, double>();
            Dictionary<string,string> sentenceAuthors = new Dictionary<string, string>();
            Dictionary<string,string> sentenceCommentTrees = new Dictionary<string, string>();
            Dictionary<string,int> sentenceIds = new Dictionary<string, int>();
            foreach (children child in children)
            {
                try
                {
                    Dictionary<string, HashSet<int>> wordIDMapping = GetWordIDMapping(child);
                    string text = child.text;
                    List<string> currSentences = SentenceTokenizer.Tokenize(Util.StripTagsCharArray(text));
                    string bestSentence = currSentences[0];
                    double currMax = double.MinValue;
                    foreach (string sentence in currSentences)
                    {

                        string[] allWords = GetAllWords(sentence);
                        bool goodSentence = (allWords.Length > 2) && (stopWords.Where(x => !allWords.Contains(x.ToLower())).Count() > 2);
                        if (goodSentence)
                        {
                            double weightedScore = 0;
                            int totalIDCount = 0;
                            foreach (string word in allWords)
                            {
                                if (!stopWords.Contains(word.ToLower()))
                                {
                                    string stemmedWord = Stemmer.GetStem(word);
                                    if (wordIDMapping.ContainsKey(stemmedWord))
                                    {
                                        HashSet<int> idsContainingWord = wordIDMapping[stemmedWord];
                                        totalIDCount += idsContainingWord.Count;
                                        weightedScore += idsContainingWord.Count * 1.0/(CommonWords.GetFrequency(word) + 1);
                                    }
                                }
                            }
                            //add some weighting so that longer sentences have more weight
                            weightedScore = weightedScore*(1 - (1/(Math.Pow(1.25, allWords.Length))));
                            double avgScore = weightedScore / allWords.Length;
                            if (avgScore > currMax)
                            {
                                currMax = avgScore;
                                bestSentence = sentence;
                            }
                        }
                    }
                    sentenceScores[bestSentence] = currMax;
                    sentenceAuthors[bestSentence] = child.author;
                    sentenceCommentTrees[bestSentence] = JsonConvert.SerializeObject(GetCommentTreeString(child));
                    sentenceIds[bestSentence] = child.id;
                }
                catch (Exception ex)
                {
                    
                }
            }
            topSentences = sentenceScores.OrderByDescending(x => x.Value).Take(N).Where(y=>!string.IsNullOrWhiteSpace(y.Key)).Select(x=>x.Key).ToList();
            foreach (var sent in topSentences)
            {
                SentenceObj sentenceObj = new SentenceObj()
                {
                    Author = sentenceAuthors[sent], Sentence = sent, SentenceCommentTree = sentenceCommentTrees[sent],Id = sentenceIds[sent],StoryId = this.id
                };
                topSentenceObjs.Add(sentenceObj);   
            }
            topSentenceObjs = topSentenceObjs.OrderByDescending(x => GetChildCount(GetNodeById(x.Id))).ToList();
            return topSentenceObjs;
        }

        Dictionary<string, HashSet<int>> GetWordIDMapping()
        {
            Dictionary<string, HashSet<int>> wordIDMapping = new Dictionary<string, HashSet<int>>();
            foreach (children child in children)
            {
                Dictionary<string, HashSet<int>> mapping = GetWordIDMapping(child);
                foreach (var kvp in mapping)
                {
                    if (wordIDMapping.ContainsKey(kvp.Key))
                    {
                        wordIDMapping[kvp.Key].UnionWith(kvp.Value);
                    }
                    else
                    {
                        wordIDMapping[kvp.Key] = kvp.Value;
                    }
                }
            }
            return wordIDMapping;
        }

        Dictionary<string, HashSet<int>> GetWordIDMapping(children child)
        {
            Dictionary<string, HashSet<int>> wordIDMapping = new Dictionary<string, HashSet<int>>();
            string[] allWords = GetAllWords(child.text);
            
            foreach (string word in allWords)
            {
                if(stopWords.Contains(word.ToLower())) continue;
                if (word.Length < 3 && word.Any(c=>char.IsLower(c))) continue;
                string stem = Stemmer.GetStem(word);
                if (!wordIDMapping.ContainsKey(stem))
                {
                    wordIDMapping[stem] = new HashSet<int>();
                }
                wordIDMapping[stem].Add(child.id);
            }
            foreach (children childitem in child.Children)
            {
                Dictionary<string, HashSet<int>> mapping = GetWordIDMapping(childitem);
                foreach (var kvp in mapping)
                {
                    if (wordIDMapping.ContainsKey(kvp.Key))
                    {
                        wordIDMapping[kvp.Key].UnionWith(kvp.Value);
                    }
                    else
                    {
                        wordIDMapping[kvp.Key] = kvp.Value;
                    }
                }
            }
            return wordIDMapping;
        }

        private Dictionary<string, string> GetStemParentDictionary(string[] allWords)
        {
            Dictionary<string,string> stemParentDictionary = new Dictionary<string, string>();
            Dictionary<string,int> parentWordCount = new Dictionary<string, int>();
            foreach (string word in allWords)
            {
                string stem = Stemmer.GetStem(word);
                if (!parentWordCount.ContainsKey(word))
                {
                    parentWordCount[word] = 0;
                }
                parentWordCount[word] += 1;
                if (stemParentDictionary.ContainsKey(stem))
                {
                    if (parentWordCount[word] > parentWordCount[stemParentDictionary[stem]])
                    {
                        stemParentDictionary[stem] = word;
                    }
                }
                else
                {
                    stemParentDictionary[stem] = word;
                }
            }
            return stemParentDictionary;
        } 

        private void PopulateNodeList(children child)
        {
            NodeList.Add(child);
            foreach (children childnode in child.Children)
            {
                PopulateNodeList(childnode);
            }
        }

        public int GetChildCount(children childrenlist)
        {
            int counter = string.IsNullOrWhiteSpace(childrenlist.text)?0:1;
            foreach (children child in childrenlist.Children)
            {
                counter += GetChildCount(child);
            }
            return counter;
        }

        public int GetStoryCommentCount()
        {
            int counter = 0;
            foreach (children child in this.children)
            {
                int childcount = GetChildCount(child);
                counter += childcount;
            }
            return counter;
        }

        public string GetCommentTree()
        {
            string commentTree = string.Empty;
            commentTree = JsonConvert.SerializeObject(this);
            return commentTree;
        }

        public TagCloudNode GetTagCloudTreeString(children children)
        {
            Dictionary<string, int> topNWordsRoot = children.GetTopNWordsDictionary(10);
            string topNWords = GetTagCloudFromDictionary(topNWordsRoot);
            TagCloudNode tgnRoot = new TagCloudNode();
            tgnRoot.id = children.id;
            tgnRoot.key = children.key;
            tgnRoot.text = topNWords;
            tgnRoot.title = topNWords;
            
            tgnRoot.children = new List<TagCloudNode>();
            children.Children = children.Children.OrderByDescending(x => x.created_at).ToList();
            foreach (children child in children.Children)
            {
                if (!string.IsNullOrWhiteSpace(child.SubtreeText))
                {
                    TagCloudNode tgnChild = GetTagCloudTreeString(child);
                    tgnRoot.children.Add(tgnChild);
                }
            }
            return tgnRoot;
        }

        public string GetTagCloudTreeString()
        {
            FullStory fs = this;
            Dictionary<string, int> topNWordsRoot = fs.GetTopNWordsDictionary(10);
            TagCloudNode tgnRoot = new TagCloudNode();
            string topNWords = GetTagCloudFromDictionary(topNWordsRoot);
            tgnRoot.id = fs.id;
            tgnRoot.key = fs.id;
            tgnRoot.text = topNWords;
            tgnRoot.title = topNWords;
            
            tgnRoot.children = new List<TagCloudNode>();
            //sort like HN
            //fs.children =
            //    fs.children.OrderByDescending(x => (x.score - 1)/Math.Pow((DateTime.Now.Subtract(x.created_at).TotalHours+2),1.5))
            //        .ToList();
            fs.children = fs.children.OrderByDescending(x => x.created_at).ToList();
            foreach (children child in fs.children)
            {
                if (!string.IsNullOrWhiteSpace(child.SubtreeText))
                {
                    TagCloudNode tgnChild = GetTagCloudTreeString(child);
                    tgnRoot.children.Add(tgnChild);
                }
            }
            return JsonConvert.SerializeObject(tgnRoot);
        }

        public string GetCommentTreeString()
        {
            FullStory fs = this;
            Dictionary<string, int> topNWordsRoot = fs.GetTopNWordsDictionary(10);
            TagCloudNode tgnRoot = new TagCloudNode();
            tgnRoot.id = fs.id;
            tgnRoot.key = fs.id;
            tgnRoot.text = fs.title;
            tgnRoot.title = fs.title;
            GetTagCloudFromDictionary(topNWordsRoot);
            tgnRoot.children = new List<TagCloudNode>();
            //sort like HN
            //fs.children =
            //    fs.children.OrderByDescending(x => (x.score - 1)/Math.Pow((DateTime.Now.Subtract(x.created_at).TotalHours+2),1.5))
            //        .ToList();
            fs.children = fs.children.OrderByDescending(x => x.created_at).ToList();
            foreach (children child in fs.children)
            {
                if (!string.IsNullOrWhiteSpace(child.SubtreeText))
                {
                    TagCloudNode tgnChild = GetCommentTreeString(child);
                    tgnRoot.children.Add(tgnChild);
                }
            }
            return JsonConvert.SerializeObject(tgnRoot);
        }

        public IEnumerable <KeyValuePair<string, List<CommentObj>>> GetUserComments()
        {
            UserComments = new Dictionary<string, List<CommentObj>>();
            foreach (children childNode in children)
            {
                try
                {
                    LoadUserComments(childNode);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                } 
            }
            var sortedDict = from entry in UserComments orderby entry.Key select entry;
            return sortedDict;
        }

        void LoadUserComments(children child)
        {
            if (!string.IsNullOrWhiteSpace(child.author))
            {
                if (!UserComments.ContainsKey(child.author))
                {
                    UserComments[child.author] = new List<CommentObj>();
                }
                UserComments[child.author].Add(new CommentObj() { Id = child.id, Text = child.text });
            }
            foreach (children childNode in child.Children)
            {
                LoadUserComments(childNode);
            }
        }

        public TagCloudNode GetCommentTreeString(children children)
        {
            Dictionary<string, int> topNWordsRoot = children.GetTopNWordsDictionary(10);
            TagCloudNode tgnRoot = new TagCloudNode();
            tgnRoot.id = children.id;
            tgnRoot.key = children.key;
            tgnRoot.text = children.text;
            bool isOP = author == children.author;
            string citePre = isOP ? "<cite class='op'>" : "<cite>";
            string citePost = ":</cite>";
            tgnRoot.title = citePre+children.author+ citePost+children.text;
            GetTagCloudFromDictionary(topNWordsRoot);
            tgnRoot.children = new List<TagCloudNode>();
            children.Children = children.Children.OrderByDescending(x => x.created_at).ToList();
            foreach (children child in children.Children)
            {
                if (!string.IsNullOrWhiteSpace(child.SubtreeText))
                {
                    TagCloudNode tgnChild = GetCommentTreeString(child);
                    tgnRoot.children.Add(tgnChild);
                }
            }
            return tgnRoot;
        }

        string GetTagCloudFromDictionary(Dictionary<string, int> dict)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in dict)
            {
                double fontSize = ((Math.Min(item.Value, 10) + 5) * 100) / 10;
                string[] separators = { "n't", "'m", "'ll", "'d", "'ve", "'re", "'s" };
                string actualWord = item.Key.Split(separators, StringSplitOptions.None)[0];
                double weight = Math.Log(item.Value, 2);
                double frequencyLog = Math.Log(CommonWords.GetFrequency(actualWord),2);
                double actualFontSize = fontSize*(1.5 - (frequencyLog*1.0/25));
                if (actualFontSize > 0)
                {
                    sb.Append("<span style='font-size:");
                    sb.Append(actualFontSize);
                    sb.Append("%'>");
                    sb.Append(item.Key);
                    sb.Append("</span>&nbsp;");
                }
            }
            return sb.ToString();
        }

        string[] GetAllWords(string text)
        {
            text = WebUtility.HtmlDecode(text);
            string tagLess = Util.StripTagsCharArray(text);
            string urlLess = Regex.Replace(tagLess,
                @"((http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)",
                string.Empty);
            string[] separators = { " ", ".", ",", ";", "-", "(", ")", "[", "]", "*", "#", "$", "%", "\"","?","!",":","\n","\r" };
            string[] allWords = urlLess.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            return allWords;
        }

        public List<string> GetAnchorWords(int N)
        {
            StringBuilder sbAllWords = new StringBuilder();
            foreach (children child in children)
            {
                sbAllWords.Append(child.SubtreeText);
                sbAllWords.Append(" ");
            }
            string[] allWords = GetAllWords(sbAllWords.ToString());
            Dictionary<string, string> stemParentDictionary = GetStemParentDictionary(allWords);
            List<string> anchorWords = new List<string>();
            children rootNode = new children();
            List<HashSet<int>> rootChildIDs = new List<HashSet<int>>();
            foreach (children child in children)
            {
                GetChildIDHashSetList(child);
                HashSet<int> currChildIDs = new HashSet<int>();
                currChildIDs.Add(child.id);
                foreach (var item in child.ChildIDList)
                {
                    currChildIDs.UnionWith(item);
                }
                rootChildIDs.Add(currChildIDs);
            }
            rootNode.ChildIDList = rootChildIDs;
            NodeList = new List<children>();
            NodeList.Add(rootNode);
            foreach (children child in children)
            {
                PopulateNodeList(child);
            }
            Dictionary<string, HashSet<int>> wordIDMapping = GetWordIDMapping();
            //Dictionary<string, double> WordTreeScore = new Dictionary<string, double>();
            Dictionary<string,List<children>> WordLCAList = new Dictionary<string, List<children>>();
            foreach (var kvp in wordIDMapping)
            {
                List<children> currLCAList = new List<children>();
                int numLCAs = 0;
                foreach (children node in NodeList)
                {
                    
                    int numBranchesWithWord = 0;
                    foreach (var childIDBranch in node.ChildIDList)
                    {
                        if (childIDBranch.Intersect(kvp.Value).Count() > 0)
                        {
                            numBranchesWithWord += 1;
                        }
                    }
                    if ((numBranchesWithWord==1 && node.ChildIDList.Count==1)|| numBranchesWithWord > 1)
                    {
                        currLCAList.Add(node);
                    }
                }
                WordLCAList[stemParentDictionary.ContainsKey(kvp.Key)? stemParentDictionary[kvp.Key]:kvp.Key] = currLCAList;
            }
            anchorWords = WordLCAList
                .OrderByDescending(x => x.Value.Count)
                .Select(x => x.Key)
                .Where(y => CommonWords.GetFrequency(y) < 20)
                .Where(z=>!(z.EndsWith("n't") || z.EndsWith("'m")||(z.EndsWith("'ll"))||(z.EndsWith("'d"))||z.EndsWith("'ve")||z.EndsWith("'re")||z.EndsWith("'s")))
                .Take(N)
                .ToList();
            return anchorWords;
        }

        public Dictionary<string, List<CommentObj>> GetNamedObjects(int N)
        {
            StringBuilder sbAllWords = new StringBuilder();
            foreach (children child in children)
            {
                sbAllWords.Append(child.SubtreeText);
                sbAllWords.Append(" ");
            }
            string[] allWords = GetAllWords(sbAllWords.ToString());
            Dictionary<string, string> stemParentDictionary = GetStemParentDictionary(allWords);
            List<string> namedObjects = new List<string>();
            children rootNode = new children();
            List<HashSet<int>> rootChildIDs = new List<HashSet<int>>();
            foreach (children child in children)
            {
                GetChildIDHashSetList(child);
                HashSet<int> currChildIDs = new HashSet<int>();
                currChildIDs.Add(child.id);
                foreach (var item in child.ChildIDList)
                {
                    currChildIDs.UnionWith(item);
                }
                rootChildIDs.Add(currChildIDs);
            }
            rootNode.ChildIDList = rootChildIDs;
            NodeList = new List<children>();
            NodeList.Add(rootNode);
            foreach (children child in children)
            {
                PopulateNodeList(child);
            }
            Dictionary<string, HashSet<int>> wordIDMapping = GetWordIDMapping();
            //Dictionary<string, double> WordTreeScore = new Dictionary<string, double>();
            Dictionary<string, List<children>> WordLCAList = new Dictionary<string, List<children>>();
            foreach (var kvp in wordIDMapping)
            {
                List<children> currLCAList = new List<children>();
                int numLCAs = 0;
                foreach (children node in NodeList)
                {
                    int numBranchesWithWord = 0;
                    foreach (var childIDBranch in node.ChildIDList)
                    {
                        if (childIDBranch.Intersect(kvp.Value).Count() > 0)
                        {
                            numBranchesWithWord += 1;
                        }
                    }
                    if ((numBranchesWithWord == 1 && node.ChildIDList.Count == 1) || numBranchesWithWord > 1)
                    {
                        currLCAList.Add(node);
                    }
                }
                WordLCAList[stemParentDictionary.ContainsKey(kvp.Key) ? stemParentDictionary[kvp.Key] : kvp.Key] = currLCAList;
            }
            namedObjects = WordLCAList
                .OrderByDescending(x => x.Value.Count)
                .Select(x => x.Key)
                .Where(y => CommonWords.GetFrequency(y) < 1)
                .Where(a=>char.IsUpper(a[0]))
                .Where(b => b.Length>1)
                .Where(z => !(z.EndsWith("n't") || z.EndsWith("'m") || (z.EndsWith("'ll")) || (z.EndsWith("'d")) || z.EndsWith("'ve") || z.EndsWith("'re") || z.EndsWith("'s")))
                .Take(N)
                .ToList();
            //namedObjects.Sort();
            Dictionary<string,List<CommentObj>> namedObjectDictionary = new Dictionary<string, List<CommentObj>>();
            foreach (string namedObject in namedObjects)
            {
                List<CommentObj> commentObjsForWord = new List<CommentObj>();
                string stem = Stemmer.GetStem(namedObject);
                HashSet<int> idsWithWord = wordIDMapping[stem];
                foreach (int id in idsWithWord)
                {
                    children child = GetNodeById(id);
                    CommentObj commentObj = new CommentObj(){Id = id,Text = child.text};
                    commentObjsForWord.Add(commentObj);
                }
                namedObjectDictionary[namedObject] = commentObjsForWord;
            }
            var ordered = namedObjectDictionary.Keys.OrderByDescending(x => namedObjectDictionary[x].Count).ToList().ToDictionary(x=>x,x=>namedObjectDictionary[x]);
            return ordered;
        }

        public children GetNodeById(int nodeid)
        {
            NodeList = new List<children>();
            foreach (children child in children)
            {
                PopulateNodeList(child);
            }
            return NodeList.FirstOrDefault(x => x.id == nodeid);
        }

        public List<string> GetAnchorWords(children root, int N)
        {
            List<string> anchorWords = new List<string>();
            string[] allWords = GetAllWords(root.SubtreeText);
            Dictionary<string, string> stemParentDictionary = GetStemParentDictionary(allWords);
            children rootNode = new children();
            List<HashSet<int>> rootChildIDs = new List<HashSet<int>>();
            foreach (children child in root.Children)
            {
                GetChildIDHashSetList(child);
                HashSet<int> currChildIDs = new HashSet<int>();
                currChildIDs.Add(child.id);
                foreach (var item in child.ChildIDList)
                {
                    currChildIDs.UnionWith(item);
                }
                rootChildIDs.Add(currChildIDs);
            }
            rootNode.ChildIDList = rootChildIDs;
            NodeList = new List<children>();
            NodeList.Add(rootNode);
            foreach (children child in root.Children)
            {
                PopulateNodeList(child);
            }
            Dictionary<string, HashSet<int>> wordIDMapping = GetWordIDMapping();
            //Dictionary<string, double> WordTreeScore = new Dictionary<string, double>();
            Dictionary<string, List<children>> WordLCAList = new Dictionary<string, List<children>>();
            foreach (var kvp in wordIDMapping)
            {
                List<children> currLCAList = new List<children>();
                int numLCAs = 0;
                foreach (children node in NodeList)
                {

                    int numBranchesWithWord = 0;
                    foreach (var childIDBranch in node.ChildIDList)
                    {
                        if (childIDBranch.Intersect(kvp.Value).Count() > 0)
                        {
                            numBranchesWithWord += 1;
                        }
                    }
                    if ((numBranchesWithWord == 1 && node.ChildIDList.Count == 1) || numBranchesWithWord > 1)
                    {
                        currLCAList.Add(node);
                    }
                }
                WordLCAList[stemParentDictionary.ContainsKey(kvp.Key) ? stemParentDictionary[kvp.Key] : kvp.Key] = currLCAList;
            }
            anchorWords = WordLCAList.OrderByDescending(x => x.Value.Count).Select(x => x.Key).Where(y=>CommonWords.GetFrequency(y)<20).Take(N).ToList();
            return anchorWords;
        }

        private void GetChildIDHashSetList(children node)
        {
            node.ChildIDList = GetChildIDList(node);
            foreach (children child in node.Children)
            {
                GetChildIDHashSetList(child);
            }
        }

        private List<HashSet<int>> GetChildIDList(children node)
        {
            List<HashSet<int>> myChildren = new List<HashSet<int>>();
            foreach (children child in node.Children)
            {
                HashSet<int> idList = GetChildIDs(child);
                myChildren.Add(idList);
            }
            return myChildren;
        }

        private HashSet<int> GetChildIDs(children childnode)
        {
            HashSet<int> childIDs = new HashSet<int>();
            childIDs.Add(childnode.id);
            foreach (children child in childnode.Children)
            {
                childIDs.Add(child.id);
                childIDs.UnionWith(GetChildIDs(child));
            }
            return childIDs;
        } 

        public Dictionary<string, int> GetTopNWordsDictionary(int N)
        {
            string[] ignoreWords = {"*"};
            
            Dictionary<string, int> wordCount = new Dictionary<string, int>();
            StringBuilder sbFullText = new StringBuilder();
            foreach (children child in this.children)
            {
                sbFullText.Append(child.SubtreeText);
                sbFullText.Append(" ");
            }
            string[] allWords = GetAllWords(sbFullText.ToString());
            wordCount = new Dictionary<string, int>();
            
            
            Dictionary<string, string> stemParent = new Dictionary<string, string>();
            foreach (string word in allWords)
            {
                try
                {
                    string stemmed = Stemmer.GetStem(word);
                    if (stemParent.ContainsKey(stemmed))
                    {
                        if (stemParent[stemmed].Length < word.Length)
                        {
                            stemParent[stemmed] = word;
                        }
                    }
                    else
                    {
                        stemParent[stemmed] = word;
                    }
                    if (stopWords.Contains(stemmed.ToLower())) continue;
                    if (!wordCount.ContainsKey(stemmed) && !ignoreWords.Contains(stemmed))
                    {
                        wordCount[stemmed] = 1;
                    }
                    else
                    {
                        wordCount[stemmed] += 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            wordCount = wordCount.OrderByDescending(x => x.Value).Take(N).ToDictionary(kvp=>stemParent[kvp.Key],kvp=>kvp.Value);
            return wordCount;
        } 

        public List<string> GetTopNWords(int N)
        {
            string[] allWords;
            Dictionary<string, int> wordCount = new Dictionary<string, int>();
            List<string> topNWords = new List<string>();
            List<string> topNWordsForComment = new List<string>();
            StringBuilder sbFullText = new StringBuilder();
            foreach (children child in this.children)
            {
                sbFullText.Append(child.SubtreeText);
                sbFullText.Append(" ");
            }
            string tagLess = Util.StripTagsCharArray(sbFullText.ToString());
            wordCount = new Dictionary<string, int>();
            string[] separators = { " ", "." };
            allWords = tagLess.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in allWords)
            {
                if (stopWords.Contains(word.ToLower())) continue;
                if (!wordCount.ContainsKey(word))
                {
                    wordCount[word] = 1;
                }
                else
                {
                    wordCount[word] += 1;
                }
            }
            topNWordsForComment = wordCount.OrderByDescending(x => x.Value).Select(x => x.Key + "[" + x.Value + "]").Take(N).ToList();
            topNWords.AddRange(topNWordsForComment);
            return topNWords;
        }
    }
}
